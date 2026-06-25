using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace KupoUI.PR.Textures;

internal static class TextureResolver
{
    private static readonly Dictionary<string, string> TexturePathIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AmbiguousTextureNames = new(StringComparer.OrdinalIgnoreCase);
    // Path-based index: keys are normalised "GameAssets/…/Name" strings (no extension, forward slashes).
    // Populated for any file whose disk path contains a "GameAssets" folder segment inside the layer.
    private static readonly Dictionary<string, string> PathTextureIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TextureOverrideMetadata> MetadataCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Sprite> SpriteCache = new();
    private static readonly HashSet<int> InPlaceProcessedTextureIds = new();
    private static readonly HashSet<string> NonReadableTextureWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ReloadLock = new();

    private static bool _initialized;
    private static bool _verboseLogs;
    private static string _currentGameTag = "Shared";
    private static string _textureRootPath = string.Empty;
    private static string _uiFramesPack = "Default";
    private static string _uiBgColorPack = "Default";
    private static string _cursorsPack = "Default";
    private static string _buttonPromptsPack = "Default";
    private static FileSystemWatcher _watcher;
    private static int _reindexRequested;
    private static long _lastChangeUtcTicks;
    private static int _hotReloadDebounceMs = 350;

    internal static void Initialize(
        string configuredRootPath,
        string gameTagOverride,
        string uiFramesPack,
        string uiBgColorPack,
        string cursorsPack,
        string buttonPromptsPack,
        bool verboseLogs)
    {
        _verboseLogs = verboseLogs;
        _currentGameTag = GameTagDetector.Detect(gameTagOverride);
        _textureRootPath = ResolveTextureRootPath(configuredRootPath);
        _uiFramesPack = NormalizePackFolderName(uiFramesPack);
        _uiBgColorPack = NormalizePackFolderName(uiBgColorPack);
        _cursorsPack = NormalizePackFolderName(cursorsPack);
        _buttonPromptsPack = NormalizePackFolderName(buttonPromptsPack);

        EnsureFolderSkeleton(_textureRootPath);
        BuildIndex(_textureRootPath, _currentGameTag);
        SetupHotReloadWatcher();

        _initialized = true;

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"Texture resolver ready. GameTag={_currentGameTag}, Root={_textureRootPath}, Indexed={TexturePathIndex.Count}, UIFrames={_uiFramesPack}, UIBackground={_uiBgColorPack}, Cursors={_cursorsPack}, ButtonPrompts={_buttonPromptsPack}");
    }

    internal static string NormalizeName(string textureName)
    {
        if (string.IsNullOrEmpty(textureName))
        {
            return textureName;
        }

        return textureName.Replace("(Clone)", string.Empty)
            .Replace("(Instance)", string.Empty)
            .Trim();
    }

    internal static bool IsLikelyAtlasTextureName(string textureName)
    {
        var name = NormalizeName(textureName);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.StartsWith("sactx-", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_atlas", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("atlas", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool HasTextureOverride(string textureName)
    {
        MaybeRefreshIndex();

        var key = NormalizeName(textureName);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return TexturePathIndex.ContainsKey(key);
    }

    internal static bool HasPathOverride(string assetAddress)
    {
        MaybeRefreshIndex();
        return TryGetFilePathByAddress(assetAddress, out _);
    }

    internal static bool TryReplaceTextureInPlace(Texture2D originalTexture, string textureName, string assetAddressHint = null)
    {
        MaybeRefreshIndex();

        if (!_initialized || originalTexture == null || string.IsNullOrEmpty(textureName))
        {
            return false;
        }

        var textureId = originalTexture.GetInstanceID();
        if (InPlaceProcessedTextureIds.Contains(textureId))
        {
            return false;
        }

        var key = NormalizeName(textureName);
        TextureLogger.LogObservedTextureName(key, "TryReplaceTextureInPlace");

        string path;
        if (!string.IsNullOrEmpty(assetAddressHint) && TryGetFilePathByAddress(assetAddressHint, out var addressedPath))
        {
            path = addressedPath;
        }
        else if (!TryGetFilePathByName(key, out path))
        {
            TextureLogger.LogMissingTexture(key, "TryReplaceTextureInPlace");
            return false;
        }

        var metadata = LoadTextureMetadata(path);

        try
        {
            var bytes = File.ReadAllBytes(path);

            var extension = Path.GetExtension(path);
            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                if (!KupoUIPRPlugin.EnableDDSTexturesConfig.Value)
                {
                    return false;
                }

                if (!DdsTextureLoader.TryLoadTexture(bytes, key, out var ddsTexture) || ddsTexture == null)
                {
                    return false;
                }

                if (!CopyTextureInPlace(originalTexture, ddsTexture))
                {
                    return false;
                }
            }
            else if (!ImageConversion.LoadImage(originalTexture, bytes))
            {
                return false;
            }

            originalTexture.filterMode = ResolveFilterMode(path, metadata);
            originalTexture.wrapMode = TextureWrapMode.Clamp;
            originalTexture.Apply(true, false);

            if (!originalTexture.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
            {
                originalTexture.name = key + "_Custom";
            }

            InPlaceProcessedTextureIds.Add(textureId);

            if (_verboseLogs)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] In-place replacement: {key} -> {path}");
            }

            TextureLogger.LogResolvedTexture(key, path, "TryReplaceTextureInPlace");

            return true;
        }
        catch (Exception ex)
        {
            if (IsNotReadableTextureException(ex))
            {
                if (NonReadableTextureWarnings.Add(key))
                {
                    if (_verboseLogs)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Skipping in-place replacement for non-readable texture: {key}");
                    }
                }

                return false;
            }

            KupoUIPRPlugin.PluginLog.LogWarning($"[TextureResolver] Failed in-place replacement for {key}: {ex.Message}");
            return false;
        }
    }

    internal static bool TryCreateReplacementSprite(Sprite original, out Sprite replacement, string assetAddressHint = null)
    {
        MaybeRefreshIndex();

        replacement = null;
        if (!_initialized || original == null || original.texture == null)
        {
            return false;
        }

        var originalId = original.GetInstanceID();
        if (SpriteCache.TryGetValue(originalId, out var cached) && cached != null)
        {
            replacement = cached;
            return true;
        }

        var spriteName = NormalizeName(original.name);
        var textureName = NormalizeName(original.texture.name);

        TextureLogger.LogObservedTextureName(spriteName, "TryCreateReplacementSprite:sprite");
        TextureLogger.LogObservedTextureName(textureName, "TryCreateReplacementSprite:texture");

        var customTexture = LoadTexture(spriteName, assetAddressHint, out var metadata);
        if (customTexture == null && !IsLikelyAtlasTextureName(textureName))
        {
            customTexture = LoadTexture(textureName, assetAddressHint, out metadata);
        }

        if (customTexture == null)
        {
            return false;
        }

        var rect = ResolveReplacementRect(original, customTexture, metadata);

        var pivot = original.rect.width > 0f && original.rect.height > 0f
            ? new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height)
            : new Vector2(0.5f, 0.5f);

        var replacementPixelsPerUnit = CalculateReplacementPixelsPerUnit(original, rect, metadata);

        replacement = Sprite.Create(
            customTexture,
            rect,
            pivot,
            replacementPixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            original.border);

        replacement.name = spriteName + "_Custom";
        UnityEngine.Object.DontDestroyOnLoad(customTexture);
        UnityEngine.Object.DontDestroyOnLoad(replacement);

        SpriteCache[originalId] = replacement;

        var shouldLogDiagnostics = _verboseLogs || KupoUIPRPlugin.IsTextureLoggerEnabled;
        if (shouldLogDiagnostics)
        {
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[TextureResolver] Sprite replacement: {spriteName} / {textureName} | rect=({rect.x:0.##},{rect.y:0.##},{rect.width:0.##},{rect.height:0.##}) ppu={replacementPixelsPerUnit:0.###} filter={customTexture.filterMode} meta={DescribeMetadata(metadata)}");
        }

        return true;
    }

    private static float CalculateReplacementPixelsPerUnit(Sprite original, Rect replacementRect, TextureOverrideMetadata metadata)
    {
        if (original == null)
        {
            return 100f;
        }

        if (metadata != null && metadata.PixelsPerUnit > 0f)
        {
            return metadata.PixelsPerUnit;
        }

        var originalPixelsPerUnit = original.pixelsPerUnit > 0f ? original.pixelsPerUnit : 100f;
        var originalRect = original.rect;
        var targetWidth = metadata != null && metadata.Width > 0 ? metadata.Width : originalRect.width;
        var targetHeight = metadata != null && metadata.Height > 0 ? metadata.Height : originalRect.height;

        if (targetWidth <= 0f || targetHeight <= 0f
            || replacementRect.width <= 0f || replacementRect.height <= 0f)
        {
            return originalPixelsPerUnit;
        }

        var widthRatio = replacementRect.width / targetWidth;
        var heightRatio = replacementRect.height / targetHeight;

        // Sidecar width/height can override the source sprite's logical display size.
        // When absent, fall back to the original sprite rect dimensions.
        var dominantRatio = Math.Max(widthRatio, heightRatio);
        if (dominantRatio <= 0f)
        {
            return originalPixelsPerUnit;
        }

        return originalPixelsPerUnit * dominantRatio;
    }

    private static Rect ResolveReplacementRect(Sprite original, Texture2D replacementTexture, TextureOverrideMetadata metadata)
    {
        var rect = original.rect;
        if (rect.x < 0f || rect.y < 0f || rect.width <= 0f || rect.height <= 0f
            || rect.xMax > replacementTexture.width || rect.yMax > replacementTexture.height)
        {
            // Fallback for standalone replacement textures that do not match original atlas coordinates.
            rect = new Rect(0f, 0f, replacementTexture.width, replacementTexture.height);
        }

        if (metadata == null || (metadata.Width <= 0 && metadata.Height <= 0))
        {
            return rect;
        }

        var targetWidth = metadata.Width > 0 ? metadata.Width : rect.width;
        var targetHeight = metadata.Height > 0 ? metadata.Height : rect.height;

        if (targetWidth <= 0f || targetHeight <= 0f)
        {
            return rect;
        }

        // Prefer preserving atlas origin when the requested size fits there.
        if (rect.x >= 0f
            && rect.y >= 0f
            && rect.x + targetWidth <= replacementTexture.width
            && rect.y + targetHeight <= replacementTexture.height)
        {
            return new Rect(rect.x, rect.y, targetWidth, targetHeight);
        }

        // Otherwise use the requested size from origin, clamped to texture bounds.
        return new Rect(
            0f,
            0f,
            Mathf.Min(targetWidth, replacementTexture.width),
            Mathf.Min(targetHeight, replacementTexture.height));
    }

    private static Texture2D LoadTexture(string textureName, string assetAddressHint, out TextureOverrideMetadata metadata)
    {
        MaybeRefreshIndex();
        metadata = null;

        if (string.IsNullOrEmpty(textureName))
        {
            return null;
        }

        string path;
        if (!string.IsNullOrEmpty(assetAddressHint) && TryGetFilePathByAddress(assetAddressHint, out var addressedPath))
        {
            path = addressedPath;
        }
        else if (!TryGetFilePathByName(textureName, out path))
        {
            TextureLogger.LogMissingTexture(textureName, "LoadTexture");
            return null;
        }

        metadata = LoadTextureMetadata(path);

        // Use address as cache key when available to prevent cross-bundle collisions.
        var cacheKey = string.IsNullOrEmpty(assetAddressHint) ? textureName : assetAddressHint;
        if (TextureCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            Texture2D texture;
            var extension = Path.GetExtension(path);

            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                if (!KupoUIPRPlugin.EnableDDSTexturesConfig.Value)
                {
                    return null;
                }

                if (!DdsTextureLoader.TryLoadTexture(bytes, textureName, out texture) || texture == null)
                {
                    return null;
                }
            }
            else
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    return null;
                }
            }

            texture.filterMode = ResolveFilterMode(path, metadata);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = textureName + "_Custom";

            TextureCache[cacheKey] = texture;
            TextureLogger.LogResolvedTexture(textureName, path, "LoadTexture");
            return texture;
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[TextureResolver] Failed texture load for {textureName}: {ex.Message}");
            return null;
        }
    }

    private static string ResolveTextureRootPath(string configuredRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return Path.Combine(Paths.GameRootPath, "KupoMods");
        }

        if (Path.IsPathRooted(configuredRootPath))
        {
            return configuredRootPath;
        }

        return Path.Combine(Paths.GameRootPath, configuredRootPath);
    }

    private static void EnsureFolderSkeleton(string root)
    {
        Directory.CreateDirectory(root);

        Directory.CreateDirectory(Path.Combine(root, "00-Mods"));
        Directory.CreateDirectory(Path.Combine(root, "01-UI-Frames", "Default"));
        Directory.CreateDirectory(Path.Combine(root, "02-UI-Background", "Default"));
        Directory.CreateDirectory(Path.Combine(root, "03-Cursors", "Default"));
        Directory.CreateDirectory(Path.Combine(root, "04-Button-Prompts", "Default"));

        // Keep old directory layouts optional for backward compatibility.
        // We do not auto-create them anymore to avoid confusing new users.
    }

    private static void BuildIndex(string root, string gameTag)
    {
        TexturePathIndex.Clear();
        AmbiguousTextureNames.Clear();
        PathTextureIndex.Clear();
        TextureCache.Clear();
        MetadataCache.Clear();
        SpriteCache.Clear();
        InPlaceProcessedTextureIds.Clear();

        var watch = Stopwatch.StartNew();

        // Legacy layout support (lowest priority): Shared/FFx and 00-Mods/Shared/FFx.
        IndexLayer(Path.Combine(root, "Shared"));
        IndexLayer(Path.Combine(root, gameTag));
        IndexLayer(Path.Combine(root, "00-Mods", "Shared"));
        IndexLayer(Path.Combine(root, "00-Mods", gameTag));

        // New layout: general overrides.
        IndexLayer(Path.Combine(root, "00-Mods"));

        // New layout: selected packs override general layer.
        IndexLayer(Path.Combine(root, "01-UI-Frames", _uiFramesPack));
        IndexLayer(Path.Combine(root, "02-UI-Background", _uiBgColorPack));
        IndexLayer(Path.Combine(root, "03-Cursors", _cursorsPack));
        IndexLayer(Path.Combine(root, "04-Button-Prompts", _buttonPromptsPack));

        watch.Stop();
        if (_verboseLogs)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Indexed {TexturePathIndex.Count} textures in {watch.ElapsedMilliseconds} ms");
        }
    }

    private static void SetupHotReloadWatcher()
    {
        try
        {
            DisposeWatcher();

            if (!KupoUIPRPlugin.EnableTextureHotReloadConfig.Value)
            {
                return;
            }

            _hotReloadDebounceMs = Math.Max(50, KupoUIPRPlugin.TextureHotReloadDebounceMsConfig.Value);

            _watcher = new FileSystemWatcher(_textureRootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnTextureFolderChanged;
            _watcher.Created += OnTextureFolderChanged;
            _watcher.Deleted += OnTextureFolderChanged;
            _watcher.Renamed += OnTextureFolderChanged;

            if (_verboseLogs)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Hot-reload watcher active. Debounce={_hotReloadDebounceMs}ms");
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[TextureResolver] Could not start texture watcher: {ex.Message}");
        }
    }

    private static void DisposeWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnTextureFolderChanged;
            _watcher.Created -= OnTextureFolderChanged;
            _watcher.Deleted -= OnTextureFolderChanged;
            _watcher.Renamed -= OnTextureFolderChanged;
            _watcher.Dispose();
        }
        catch
        {
            // Ignore dispose errors.
        }
        finally
        {
            _watcher = null;
        }
    }

    private static void OnTextureFolderChanged(object sender, FileSystemEventArgs args)
    {
        _lastChangeUtcTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _reindexRequested, 1);
    }

    private static void MaybeRefreshIndex()
    {
        if (Interlocked.CompareExchange(ref _reindexRequested, 0, 0) == 0)
        {
            return;
        }

        var elapsedMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastChangeUtcTicks)) / TimeSpan.TicksPerMillisecond;
        if (elapsedMs < _hotReloadDebounceMs)
        {
            return;
        }

        lock (ReloadLock)
        {
            if (Interlocked.CompareExchange(ref _reindexRequested, 0, 0) == 0)
            {
                return;
            }

            Interlocked.Exchange(ref _reindexRequested, 0);
            BuildIndex(_textureRootPath, _currentGameTag);

            if (_verboseLogs)
            {
                KupoUIPRPlugin.PluginLog.LogInfo("[TextureResolver] Texture index reloaded after file change.");
            }
        }
    }

    private static bool CopyTextureInPlace(Texture2D destination, Texture2D source)
    {
        try
        {
            destination.Resize(source.width, source.height, source.format, source.mipmapCount > 1);
            var raw = source.GetRawTextureData();
            destination.LoadRawTextureData(raw);
            destination.Apply(source.mipmapCount > 1, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Normalises an addressable asset address to the path key used in PathTextureIndex.
    /// Input:  "Assets/GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace"
    /// Output: "GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace"
    /// </summary>
    private static string NormalizeAddressToPathKey(string assetAddress)
    {
        if (string.IsNullOrEmpty(assetAddress))
        {
            return null;
        }

        const string assetsPrefix = "Assets/";
        var key = assetAddress.Replace('\\', '/');
        if (key.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(assetsPrefix.Length);
        }

        return key;
    }

    /// <summary>
    /// Returns the override file path for an addressable asset address using the path-based index.
    /// </summary>
    private static bool TryGetFilePathByAddress(string assetAddress, out string filePath)
    {
        filePath = null;
        var pathKey = NormalizeAddressToPathKey(assetAddress);
        if (string.IsNullOrEmpty(pathKey))
        {
            return false;
        }

        return PathTextureIndex.TryGetValue(pathKey, out filePath);
    }

    private static bool TryGetFilePathByName(string textureName, out string filePath)
    {
        filePath = null;

        var key = NormalizeName(textureName);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (AmbiguousTextureNames.Contains(key))
        {
            if (_verboseLogs)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Skipping ambiguous basename fallback for {key}; path-based match required.");
            }

            return false;
        }

        return TexturePathIndex.TryGetValue(key, out filePath);
    }

    private static TextureOverrideMetadata LoadTextureMetadata(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
        {
            return null;
        }

        if (MetadataCache.TryGetValue(texturePath, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        var metadataPath = Path.ChangeExtension(texturePath, ".json");
        if (!File.Exists(metadataPath))
        {
            MetadataCache[texturePath] = null;
            return null;
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = new TextureOverrideMetadata
            {
                Width = ReadInt(json, "width"),
                Height = ReadInt(json, "height"),
                PixelsPerUnit = ReadFloat(json, "pixelsPerUnit"),
                PointFilter = ReadBool(json, "pointFilter"),
                FilterMode = ReadString(json, "filterMode"),
                FilterType = ReadString(json, "filterType")
            };

            MetadataCache[texturePath] = metadata;
            return metadata;
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[TextureResolver] Failed to parse metadata for {texturePath}: {ex.Message}");
            MetadataCache[texturePath] = null;
            return null;
        }
    }

    private static FilterMode ResolveFilterMode(string texturePath, TextureOverrideMetadata metadata)
    {
        if (metadata != null)
        {
            var requestedFilter = !string.IsNullOrEmpty(metadata.FilterMode) ? metadata.FilterMode : metadata.FilterType;
            if (!string.IsNullOrEmpty(requestedFilter))
            {
                if (requestedFilter.Equals("Point", StringComparison.OrdinalIgnoreCase))
                {
                    return FilterMode.Point;
                }

                if (requestedFilter.Equals("Trilinear", StringComparison.OrdinalIgnoreCase))
                {
                    return FilterMode.Trilinear;
                }

                if (requestedFilter.Equals("Bilinear", StringComparison.OrdinalIgnoreCase)
                    || requestedFilter.Equals("Linear", StringComparison.OrdinalIgnoreCase))
                {
                    return FilterMode.Bilinear;
                }
            }

            if (metadata.PointFilter.HasValue)
            {
                return metadata.PointFilter.Value ? FilterMode.Point : FilterMode.Bilinear;
            }
        }

        return ShouldUsePointFilter(texturePath) ? FilterMode.Point : FilterMode.Bilinear;
    }

    private static int ReadInt(string json, string propertyName)
    {
        var match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static float ReadFloat(string json, string propertyName)
    {
        var match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
        return match.Success && float.TryParse(match.Groups[1].Value, out var value) ? value : 0f;
    }

    private static bool? ReadBool(string json, string propertyName)
    {
        var match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return bool.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static string ReadString(string json, string propertyName)
    {
        var match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void IndexLayer(string layerPath)
    {
        if (!Directory.Exists(layerPath))
        {
            return;
        }

        var layerSeenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(layerPath, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (!IsSupportedExtension(extension))
            {
                continue;
            }

            var key = NormalizeName(Path.GetFileNameWithoutExtension(file));
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!layerSeenKeys.Add(key))
            {
                AmbiguousTextureNames.Add(key);
            }
            else
            {
                // A unique file in a higher-priority layer should override lower layers,
                // even if that basename was ambiguous somewhere else previously.
                AmbiguousTextureNames.Remove(key);
            }

            TexturePathIndex[key] = file;

            // Also register by path when the file lives under a "GameAssets" folder,
            // mirroring the game's addressable address structure so that files placed at
            // e.g. <Layer>/GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace.png
            // can be looked up by their address "Assets/GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace".
            var pathKey = ExtractGameAssetsPathKey(layerPath, file);
            if (!string.IsNullOrEmpty(pathKey))
            {
                PathTextureIndex[pathKey] = file;

                if (_verboseLogs)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Path-indexed: {pathKey} -> {file}");
                }
            }
        }
    }

    private static string NormalizePackFolderName(string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return "Default";
        }

        var value = configuredValue.Trim();
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        if (value.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
        {
            return "Default";
        }

        return value;
    }

    /// <summary>
    /// If <paramref name="filePath"/> contains a "GameAssets" directory segment relative to
    /// <paramref name="layerRoot"/>, returns the normalised path key starting from that segment
    /// (e.g. "GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace").
    /// Returns null otherwise.
    /// </summary>
    private static string ExtractGameAssetsPathKey(string layerRoot, string filePath)
    {
        if (!filePath.StartsWith(layerRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = filePath.Substring(layerRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

        // Look for "GameAssets/" somewhere in the relative path.
        const string marker = "GameAssets/";
        var idx = relative.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Also accept a file directly inside a folder literally named "GameAssets".
            if (!relative.StartsWith("GameAssets", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            idx = 0;
        }

        var pathKey = relative.Substring(idx);

        // Strip file extension.
        var extDot = pathKey.LastIndexOf('.');
        if (extDot > 0)
        {
            pathKey = pathKey.Substring(0, extDot);
        }

        return pathKey;
    }

    private static bool IsSupportedExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUsePointFilter(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directoryPath))
        {
            return false;
        }

        var segments = directoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Equals("pixel", StringComparison.OrdinalIgnoreCase)
                || segments[i].Equals("pixels", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNotReadableTextureException(Exception ex)
    {
        if (ex == null)
        {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        return message.IndexOf("not readable", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("texture memory can not be accessed", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DescribeMetadata(TextureOverrideMetadata metadata)
    {
        if (metadata == null)
        {
            return "none";
        }

        var width = metadata.Width > 0 ? metadata.Width.ToString() : "-";
        var height = metadata.Height > 0 ? metadata.Height.ToString() : "-";
        var ppu = metadata.PixelsPerUnit > 0f ? metadata.PixelsPerUnit.ToString("0.###") : "-";
        var mode = !string.IsNullOrEmpty(metadata.FilterMode)
            ? metadata.FilterMode
            : !string.IsNullOrEmpty(metadata.FilterType)
                ? metadata.FilterType
                : "-";
        var point = metadata.PointFilter.HasValue ? metadata.PointFilter.Value.ToString() : "-";

        return $"w={width},h={height},ppu={ppu},mode={mode},point={point}";
    }

    private sealed class TextureOverrideMetadata
    {
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal float PixelsPerUnit { get; set; }
        internal bool? PointFilter { get; set; }
        internal string FilterMode { get; set; }
        internal string FilterType { get; set; }
    }
}
