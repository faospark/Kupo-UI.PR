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

    // [OPT-7] O(1) extension lookup — replaces five sequential string.Equals calls.
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".tga", ".dds" };

    // [OPT-8] O(1) game-tag lookup — replaces six sequential string.Equals calls in IndexLayer.
    private static readonly HashSet<string> KnownGameTags = new(StringComparer.OrdinalIgnoreCase)
        { "FF1", "FF2", "FF3", "FF4", "FF5", "FF6" };

    // [OPT-5] Allocated once instead of per NormalizePackFolderName call.
    private static readonly char[] InvalidPackNameChars = { '/', '\\', ':' };

    // [OPT-1] Pre-compiled regexes for metadata JSON parsing.
    // All property names are known at compile time, so we avoid per-call Regex construction and
    // interpretation overhead. RegexOptions.Compiled produces native IL for the pattern.
    private static readonly Regex RxWidth = new(@"""width""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxHeight = new(@"""height""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxPixelsPerUnit = new(@"""pixelsPerUnit""\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxPointFilter = new(@"""pointFilter""\s*:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxFilterMode = new(@"""filterMode""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxFilterType = new(@"""filterType""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxWrapMode = new(@"""wrapMode""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxPivot = new(@"""pivot""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxBorder = new(@"""border""\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxRectX = new(@"""rectX""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxRectY = new(@"""rectY""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxFlipHorizontal = new(@"""flipHorizontal""\s*:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxFlipX = new(@"""flipX""\s*:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool _initialized;
    private static bool _verboseLogs;
    private static string _currentGameTag = "Shared";
    private static string _textureRootPath = string.Empty;
    private static string _uiFramesPack = "Default";
    private static string _uiThemesPack = "Default";
    private static string _uiBgColorPack = "Default";
    private static string _cursorsPack = "Default";
    private static string _buttonPromptsPack = "Default";
    private static FileSystemWatcher _watcher;
    private static int _reindexRequested;
    private static long _lastChangeUtcTicks;
    private static int _hotReloadDebounceMs = 350;

    internal static string CurrentGameTag => _currentGameTag;
    internal static string UiThemesPack => _uiThemesPack;
    internal static string UiFramesPack => _uiFramesPack;
    internal static string UiBgColorPack => _uiBgColorPack;
    internal static string CursorsPack => _cursorsPack;
    internal static string ButtonPromptsPack => _buttonPromptsPack;

    internal static void Initialize(
        string configuredRootPath,
        string uiFramesPack,
        string uiThemesPack,
        string uiBgColorPack,
        string cursorsPack,
        string buttonPromptsPack)
    {
        _verboseLogs = KupoUIPRPlugin.IsTextureLoggerEnabled;
        _currentGameTag = GameTagDetector.Detect();
        _textureRootPath = ResolveTextureRootPath(configuredRootPath);
        _uiFramesPack = NormalizePackFolderName(uiFramesPack);
        _uiThemesPack = NormalizePackFolderName(uiThemesPack);
        _uiBgColorPack = NormalizePackFolderName(uiBgColorPack);
        _cursorsPack = NormalizePackFolderName(cursorsPack);
        _buttonPromptsPack = NormalizePackFolderName(buttonPromptsPack);

        EnsureFolderSkeleton(_textureRootPath);
        BuildIndex(_textureRootPath, _currentGameTag);
        SetupHotReloadWatcher();

        _initialized = true;

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"Texture resolver ready. GameTag={_currentGameTag}, Root={_textureRootPath}, Indexed={TexturePathIndex.Count}, UIFrames={_uiFramesPack}, UIBgColor={_uiBgColorPack}, Cursors={_cursorsPack}, ButtonPrompts={_buttonPromptsPack}");
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
        // [OPT-3] key is already normalized above — skip the redundant NormalizeName inside TryGetFilePathByName.
        else if (!TryGetFilePathByNormalizedName(key, out path))
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

        // For the sprite-name lookup, do not forward the atlas asset address as a hint:
        // the address belongs to the atlas texture, not the individual sprite, so passing it
        // would cause LoadTexture to resolve to the atlas file instead of the per-sprite file.
        var isAtlasSprite = IsLikelyAtlasTextureName(textureName);
        var spriteAddressHint = isAtlasSprite ? null : assetAddressHint;
        var customTexture = LoadTexture(spriteName, spriteAddressHint, out var metadata);
        if (customTexture == null && !isAtlasSprite)
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

        var metadataPivot = ParsePivot(metadata);
        if (metadataPivot.HasValue)
        {
            pivot = metadataPivot.Value;
        }

        var border = original.border;
        var metadataBorder = ParseBorder(metadata);
        if (metadataBorder.HasValue)
        {
            border = metadataBorder.Value;
        }

        var replacementPixelsPerUnit = CalculateReplacementPixelsPerUnit(original, rect, metadata);

        replacement = Sprite.Create(
            customTexture,
            rect,
            pivot,
            replacementPixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            border);

        replacement.name = spriteName + "_Custom";
        UnityEngine.Object.DontDestroyOnLoad(customTexture);
        UnityEngine.Object.DontDestroyOnLoad(replacement);

        SpriteCache[originalId] = replacement;

        // [OPT-9] _verboseLogs is set to IsTextureLoggerEnabled during Initialize — no need to re-read the config here.
        if (_verboseLogs)
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
        var targetWidth = originalRect.width;
        var targetHeight = originalRect.height;

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

        if (metadata != null && (metadata.Width > 0 || metadata.Height > 0))
        {
            var targetWidth = metadata.Width > 0 ? metadata.Width : rect.width;
            var targetHeight = metadata.Height > 0 ? metadata.Height : rect.height;

            if (targetWidth > 0f && targetHeight > 0f)
            {
                // Prefer preserving atlas origin when the requested size fits there.
                if (rect.x >= 0f
                    && rect.y >= 0f
                    && rect.x + targetWidth <= replacementTexture.width
                    && rect.y + targetHeight <= replacementTexture.height)
                {
                    rect = new Rect(rect.x, rect.y, targetWidth, targetHeight);
                }
                else
                {
                    // Otherwise use the requested size from origin, clamped to texture bounds.
                    rect = new Rect(
                        0f,
                        0f,
                        Mathf.Min(targetWidth, replacementTexture.width),
                        Mathf.Min(targetHeight, replacementTexture.height));
                }
            }
        }

        // Apply explicit rect origin overrides after size resolution.
        // rectX/rectY specify the pixel offset within the replacement texture to start sampling from.
        if (metadata != null && (metadata.RectX.HasValue || metadata.RectY.HasValue))
        {
            var overrideX = metadata.RectX.HasValue ? (float)metadata.RectX.Value : rect.x;
            var overrideY = metadata.RectY.HasValue ? (float)metadata.RectY.Value : rect.y;
            overrideX = Mathf.Clamp(overrideX, 0f, Mathf.Max(0f, replacementTexture.width - rect.width));
            overrideY = Mathf.Clamp(overrideY, 0f, Mathf.Max(0f, replacementTexture.height - rect.height));
            rect = new Rect(overrideX, overrideY, rect.width, rect.height);
        }

        return rect;
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
        // [OPT-3] Callers of LoadTexture always pass already-normalized names — use the fast overload.
        else if (!TryGetFilePathByNormalizedName(textureName, out path))
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
            texture.wrapMode = ResolveWrapMode(path, metadata);
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
            return Path.Combine(Paths.GameRootPath, "Modules");
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
        Directory.CreateDirectory(Path.Combine(root, "01-UI-Themes"));
        Directory.CreateDirectory(Path.Combine(root, "02-UI-Frames"));
        Directory.CreateDirectory(Path.Combine(root, "03-UI-BgColor"));
        Directory.CreateDirectory(Path.Combine(root, "04-UI-Cursors"));
        Directory.CreateDirectory(Path.Combine(root, "05-Button-Prompts"));

        Directory.CreateDirectory(Path.Combine(root, "Shared"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "SpeakerPortraits"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF1"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF2"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF3"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF4"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF5"));
        Directory.CreateDirectory(Path.Combine(root, "Shared", "FF6"));
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
        IndexLayer(Path.Combine(root, "Shared"), excludeGameTags: true);
        IndexLayer(Path.Combine(root, "Shared", gameTag));
        IndexLayer(Path.Combine(root, gameTag));
        IndexLayer(Path.Combine(root, "00-Mods", "Shared"));
        IndexLayer(Path.Combine(root, "00-Mods", gameTag));

        // New layout: general overrides.
        IndexLayer(Path.Combine(root, "00-Mods"));

        // UI Themes: override 00-Mods but are overridden by specific pack folders.
        IndexLayer(Path.Combine(root, "01-UI-Themes", _uiThemesPack));

        // New layout: selected packs override general layer.
        IndexLayer(Path.Combine(root, "02-UI-Frames", _uiFramesPack));
        IndexLayer(Path.Combine(root, "03-UI-BgColor", _uiBgColorPack));
        IndexLayer(Path.Combine(root, "04-UI-Cursors", _cursorsPack));
        IndexLayer(Path.Combine(root, "05-Button-Prompts", _buttonPromptsPack));

        watch.Stop();
        if (_verboseLogs)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureResolver] Indexed {TexturePathIndex.Count} textures in {watch.ElapsedMilliseconds} ms");
        }

        KupoUI.PR.Patches.SpeakerPortraitsPatch.ClearCache();
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
        // [OPT-4] Use atomic write: plain long assignment is not guaranteed atomic on 32-bit CLR.
        Interlocked.Exchange(ref _lastChangeUtcTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _reindexRequested, 1);
    }

    private static void MaybeRefreshIndex()
    {
        // [OPT-12] Volatile.Read is the correct and clearer primitive for a plain flag read.
        // CompareExchange(ref x, 0, 0) is a no-op swap used as a read, which is misleading.
        if (Volatile.Read(ref _reindexRequested) == 0)
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
            if (Volatile.Read(ref _reindexRequested) == 0)
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
        var key = NormalizeName(textureName);
        return TryGetFilePathByNormalizedName(key, out filePath);
    }

    /// <summary>
    /// Like <see cref="TryGetFilePathByName"/> but skips the <see cref="NormalizeName"/> step.
    /// Use when the caller has already normalized the key to avoid redundant string allocations.
    /// </summary>
    private static bool TryGetFilePathByNormalizedName(string normalizedKey, out string filePath)
    {
        filePath = null;

        if (string.IsNullOrEmpty(normalizedKey))
        {
            return false;
        }

        if (AmbiguousTextureNames.Contains(normalizedKey))
        {
            return false;
        }

        return TexturePathIndex.TryGetValue(normalizedKey, out filePath);
    }

    internal static TextureOverrideMetadata LoadTextureMetadata(string texturePath)
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

            // [OPT-1] Use pre-compiled static Regex instances. Each Match() call runs the compiled
            // NFA against the string once; no pattern string is built or interpreted at call time.
            var metadata = new TextureOverrideMetadata
            {
                Width = MatchInt(RxWidth.Match(json)),
                Height = MatchInt(RxHeight.Match(json)),
                PixelsPerUnit = MatchFloat(RxPixelsPerUnit.Match(json)),
                PointFilter = MatchBool(RxPointFilter.Match(json)),
                FilterMode = MatchString(RxFilterMode.Match(json)),
                FilterType = MatchString(RxFilterType.Match(json)),
                WrapMode = MatchString(RxWrapMode.Match(json)),
                Pivot = MatchString(RxPivot.Match(json)),
                Border = MatchString(RxBorder.Match(json)),
                RectX = MatchNullableInt(RxRectX.Match(json)),
                RectY = MatchNullableInt(RxRectY.Match(json)),
                FlipHorizontal = MatchBool(RxFlipHorizontal.Match(json)) ?? MatchBool(RxFlipX.Match(json))
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

    // Tiny helpers that extract a typed value from a pre-executed Match result.
    // Replaces the generic ReadInt/ReadFloat/ReadBool/ReadString helpers that built and ran
    // a new Regex object on every call.
    private static int MatchInt(Match m) =>
        m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : 0;

    private static int? MatchNullableInt(Match m) =>
        m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : (int?)null;

    private static float MatchFloat(Match m) =>
        m.Success && float.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0f;

    private static bool? MatchBool(Match m) =>
        m.Success && bool.TryParse(m.Groups[1].Value, out var v) ? v : (bool?)null;

    private static string MatchString(Match m) =>
        m.Success ? m.Groups[1].Value : null;

    internal static FilterMode ResolveFilterMode(string texturePath, TextureOverrideMetadata metadata)
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

    internal static TextureWrapMode ResolveWrapMode(string texturePath, TextureOverrideMetadata metadata)
    {
        if (metadata != null && !string.IsNullOrEmpty(metadata.WrapMode))
        {
            var requestedWrap = metadata.WrapMode;
            if (requestedWrap.Equals("Repeat", StringComparison.OrdinalIgnoreCase))
            {
                return TextureWrapMode.Repeat;
            }

            if (requestedWrap.Equals("Mirror", StringComparison.OrdinalIgnoreCase))
            {
                return TextureWrapMode.Mirror;
            }

            if (requestedWrap.Equals("MirrorOnce", StringComparison.OrdinalIgnoreCase))
            {
                return TextureWrapMode.MirrorOnce;
            }

            if (requestedWrap.Equals("Clamp", StringComparison.OrdinalIgnoreCase))
            {
                return TextureWrapMode.Clamp;
            }
        }

        return TextureWrapMode.Clamp;
    }

    /// <summary>
    /// Parses a "x,y" normalized pivot string from metadata. Returns null if absent or malformed.
    /// </summary>
    internal static Vector2? ParsePivot(TextureOverrideMetadata metadata)
    {
        if (metadata == null || string.IsNullOrEmpty(metadata.Pivot))
        {
            return null;
        }

        var parts = metadata.Pivot.Split(',');
        if (parts.Length != 2)
        {
            return null;
        }

        if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
        {
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        return null;
    }

    /// <summary>
    /// Parses a "left,bottom,right,top" 9-slice border string from metadata. Returns null if absent or malformed.
    /// Values are pixel counts and must be non-negative.
    /// </summary>
    internal static Vector4? ParseBorder(TextureOverrideMetadata metadata)
    {
        if (metadata == null || string.IsNullOrEmpty(metadata.Border))
        {
            return null;
        }

        var parts = metadata.Border.Split(',');
        if (parts.Length != 4)
        {
            return null;
        }

        if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var left)
            && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bottom)
            && float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var right)
            && float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var top))
        {
            return new Vector4(
                Mathf.Max(0f, left),
                Mathf.Max(0f, bottom),
                Mathf.Max(0f, right),
                Mathf.Max(0f, top));
        }

        return null;
    }

    private static void IndexLayer(string layerPath, bool excludeGameTags = false)
    {
        if (!Directory.Exists(layerPath))
        {
            return;
        }

        var layerSeenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // [OPT-2] EnumerateFiles is lazy (no full string[] allocation). For large mod folders
        // this avoids a potentially large upfront array that is then immediately iterated.
        foreach (var file in Directory.EnumerateFiles(layerPath, "*.*", SearchOption.AllDirectories))
        {
            if (excludeGameTags)
            {
                var relPath = file.Substring(layerPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                // [OPT-8] Extract the first segment without splitting the whole string into an array.
                var sepIdx = relPath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                var firstSegment = sepIdx >= 0 ? relPath.Substring(0, sepIdx) : relPath;
                if (KnownGameTags.Contains(firstSegment))
                {
                    continue;
                }
            }

            var extension = Path.GetExtension(file);
            // [OPT-7] O(1) HashSet lookup instead of five sequential Equals comparisons.
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            // [OPT-13] Disk filenames never contain Unity suffixes like "(Clone)" or "(Instance)",
            // so NormalizeName would be a no-op beyond the final Trim. Skip the extra Replace calls.
            var key = Path.GetFileNameWithoutExtension(file).Trim();
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

        // [OPT-5] Reuse static char array instead of allocating new[] { '/', '\\', ':' } every call.
        if (value.IndexOfAny(InvalidPackNameChars) >= 0)
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
        // [OPT-11] filePath always comes from Directory.EnumerateFiles(layerPath, ...) so it is
        // guaranteed to start with layerRoot. The StartsWith guard was always true and is not needed.
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

    private static bool IsSupportedExtension(string extension) =>
        !string.IsNullOrEmpty(extension) && SupportedExtensions.Contains(extension);

    internal static bool ShouldUsePointFilter(string filePath)
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

        // [OPT-6] Scan for "pixel"/"pixels" path segment without splitting the string into an array.
        return ContainsPathSegment(directoryPath, "pixel")
            || ContainsPathSegment(directoryPath, "pixels");
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> contains a directory segment whose name exactly
    /// equals <paramref name="segment"/> (case-insensitive). Avoids allocating a string array.
    /// </summary>
    private static bool ContainsPathSegment(string path, string segment)
    {
        var idx = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            var before = idx == 0
                || path[idx - 1] == Path.DirectorySeparatorChar
                || path[idx - 1] == Path.AltDirectorySeparatorChar;
            var end = idx + segment.Length;
            var after = end >= path.Length
                || path[end] == Path.DirectorySeparatorChar
                || path[end] == Path.AltDirectorySeparatorChar;

            if (before && after)
            {
                return true;
            }

            idx = path.IndexOf(segment, idx + 1, StringComparison.OrdinalIgnoreCase);
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
        var wrap = !string.IsNullOrEmpty(metadata.WrapMode) ? metadata.WrapMode : "-";
        var pivot = !string.IsNullOrEmpty(metadata.Pivot) ? metadata.Pivot : "-";
        var border = !string.IsNullOrEmpty(metadata.Border) ? metadata.Border : "-";
        var rectX = metadata.RectX.HasValue ? metadata.RectX.Value.ToString() : "-";
        var rectY = metadata.RectY.HasValue ? metadata.RectY.Value.ToString() : "-";

        return $"w={width},h={height},ppu={ppu},mode={mode},point={point},wrap={wrap},pivot={pivot},border={border},rectX={rectX},rectY={rectY}";
    }

    internal sealed class TextureOverrideMetadata
    {
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal float PixelsPerUnit { get; set; }
        internal bool? PointFilter { get; set; }
        internal string FilterMode { get; set; }
        internal string FilterType { get; set; }
        internal string WrapMode { get; set; }
        /// <summary>Normalized pivot "x,y" (0-1 each). E.g. "0.5,0.5" = center, "0,0" = bottom-left.</summary>
        internal string Pivot { get; set; }
        /// <summary>9-slice border in pixels "left,bottom,right,top". E.g. "4,4,4,4".</summary>
        internal string Border { get; set; }
        /// <summary>Explicit X pixel offset into the replacement texture rect. Null = inherit from original sprite rect.</summary>
        internal int? RectX { get; set; }
        /// <summary>Explicit Y pixel offset into the replacement texture rect. Null = inherit from original sprite rect.</summary>
        internal int? RectY { get; set; }
        internal bool? FlipHorizontal { get; set; }
    }
}
