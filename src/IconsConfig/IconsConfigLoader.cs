using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KupoUI.PR.IconsConfig
{
    /// <summary>
    /// Loads custom icon sprites from <c>IconsConfig.json</c> files and packs them into a single
    /// GPU texture atlas so that Unity can batch all icon draws into one call.
    ///
    /// On the first run the atlas is built from scratch and written to disk under
    /// <c>Modules/.cache/</c>. Subsequent launches reload the cached atlas in milliseconds
    /// (just two PNG reads + a JSON parse) and skip the per-file PNG decoding entirely.
    ///
    /// Cache invalidation is automatic: any source icon file that is added, removed, or modified
    /// triggers a full rebuild. The <c>.cache/</c> folder can be deleted manually to force a rebuild.
    /// </summary>
    internal static class IconsConfigLoader
    {
        private const string ConfigFileName = "IconsConfig.json";
        private const string CacheSubDir    = ".cache";
        private const int    CacheVersion   = 1;   // bump if index format changes

        // ── Runtime state ─────────────────────────────────────────────────────
        // Sprites are backed by atlas textures rather than individual Texture2Ds.
        private static readonly Dictionary<string, Sprite> _sprites =
            new(StringComparer.OrdinalIgnoreCase);

        // Atlas textures — kept referenced so GC never collects them.
        private static Texture2D _atlasPoint;
        private static Texture2D _atlasLinear;

        // ── Pre-compiled regexes ───────────────────────────────────────────────
        private static readonly Regex RxComment = new(@"//[^\r\n]*",
            RegexOptions.Compiled);
        private static readonly Regex RxKvString = new(@"""([^""]+)""\s*:\s*""([^""]+)""",
            RegexOptions.Compiled);
        // Cache-index parsers (patterns match the exact format written by TrySaveCacheToDisk).
        private static readonly Regex RxCacheSource = new(
            @"\{\s*""path""\s*:\s*""(?<p>[^""]*)""\s*,\s*""ticks""\s*:\s*(?<t>\d+)\s*\}",
            RegexOptions.Compiled);
        private static readonly Regex RxCacheIcon = new(
            @"\{\s*""tag""\s*:\s*""(?<tag>[^""]*)""\s*,\s*""x""\s*:\s*(?<x>\d+)\s*,\s*""y""\s*:\s*(?<y>\d+)\s*,\s*""w""\s*:\s*(?<w>\d+)\s*,\s*""h""\s*:\s*(?<h>\d+)\s*,\s*""point""\s*:\s*(?<pt>true|false)\s*\}",
            RegexOptions.Compiled);

        // ── Public API ────────────────────────────────────────────────────────

        internal static bool HasSprite(string tag)
            => !string.IsNullOrEmpty(tag) && GetSprite(tag) != null;

        internal static Sprite GetSprite(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;

            if (_sprites.TryGetValue(tag, out var sprite))
                return sprite;

            // Fallback: Check game's native Last.UI.IconTextUtility
            try
            {
                var nativeSprite = Last.UI.IconTextUtility.GetIcon(tag);
                if (nativeSprite != null)
                {
                    _sprites[tag] = nativeSprite; // Cache locally for fast future lookups
                    return nativeSprite;
                }
            }
            catch
            {
                // Native IconTextUtility lookup safely ignored if uninitialized or fails
            }

            if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[IconsConfig] Sprite lookup failed for tag: '{tag}'. " +
                    $"Registered keys: {string.Join(", ", _sprites.Keys)}");
            return null;
        }

        // ── Initialization ────────────────────────────────────────────────────

        internal static void Initialize(string modulesRootPath)
        {
            _sprites.Clear();
            DestroyAtlases();

            if (!Directory.Exists(modulesRootPath))
            {
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[IconsConfig] Modules root not found, skipping: {modulesRootPath}");
                return;
            }

            var gameTag    = Textures.TextureResolver.CurrentGameTag;
            var tagToFile  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Phase 1: collect tag → image-file mappings from all IconsConfig.json files.
            GatherTagMappings(modulesRootPath, gameTag, tagToFile);

            if (tagToFile.Count == 0)
            {
                if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                    KupoUIPRPlugin.PluginLog.LogInfo("[IconsConfig] No icon entries found.");
                SyncSpritesToNativeIconTextUtility();
                return;
            }

            // Phase 2: try disk cache, fall back to full atlas build.
            string cacheDir = Path.Combine(modulesRootPath, CacheSubDir);

            if (!TryLoadFromCache(cacheDir, gameTag, tagToFile))
                BuildAtlasAndCache(cacheDir, gameTag, tagToFile);

            SyncSpritesToNativeIconTextUtility();

            if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[IconsConfig] Ready: {_sprites.Count} icon sprite(s) from atlas.");
        }

        private static void SyncSpritesToNativeIconTextUtility()
        {
            try
            {
                var nativeDict = Last.UI.IconTextUtility.sprites;
                if (nativeDict == null) return;

                int synced = 0;
                foreach (var kv in _sprites)
                {
                    if (!nativeDict.ContainsKey(kv.Key))
                    {
                        nativeDict.Add(kv.Key, kv.Value);
                        synced++;
                    }
                }

                if (synced > 0 && KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[IconsConfig] Synced {synced} custom icon sprite(s) to Last.UI.IconTextUtility.");
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[IconsConfig] Could not sync custom sprites to Last.UI.IconTextUtility: {ex.Message}");
            }
        }

        // ── Phase 1: gather tag → file mappings ───────────────────────────────

        private static void GatherTagMappings(
            string modulesRootPath,
            string gameTag,
            Dictionary<string, string> tagToFile)
        {
            try
            {
                var allConfigs = Directory.GetFiles(
                    modulesRootPath, ConfigFileName, SearchOption.AllDirectories);

                foreach (var configFile in allConfigs)
                {
                    // Game-tag / block folder filtering.
                    var normalised = configFile.Replace('\\', '/');
                    var segments   = normalised.Split('/');
                    var skip       = false;

                    foreach (var seg in segments)
                    {
                        if (seg.StartsWith("block", StringComparison.OrdinalIgnoreCase))
                        { skip = true; break; }

                        bool isGameTagFolder =
                            seg.Equals("FF1", StringComparison.OrdinalIgnoreCase) ||
                            seg.Equals("FF2", StringComparison.OrdinalIgnoreCase) ||
                            seg.Equals("FF3", StringComparison.OrdinalIgnoreCase) ||
                            seg.Equals("FF4", StringComparison.OrdinalIgnoreCase) ||
                            seg.Equals("FF5", StringComparison.OrdinalIgnoreCase) ||
                            seg.Equals("FF6", StringComparison.OrdinalIgnoreCase);

                        if (isGameTagFolder &&
                            !seg.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                        { skip = true; break; }
                    }

                    if (skip) continue;

                    string json = File.ReadAllText(configFile);

                    var fileGameTag = ReadJsonString(json, "GameTag")?.Trim();
                    if (!string.IsNullOrEmpty(fileGameTag) &&
                        !fileGameTag.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileLang = ReadJsonString(json, "Language")?.Trim();
                    if (!string.IsNullOrEmpty(fileLang) &&
                        !Patches.TextConfigPatch.MatchesLanguage(fileLang))
                        continue;

                    json = RxComment.Replace(json, string.Empty);
                    var dir = Path.GetDirectoryName(configFile);

                    foreach (Match m in RxKvString.Matches(json))
                    {
                        string tag = m.Groups[1].Value;
                        if (tag.StartsWith("_", StringComparison.Ordinal) ||
                            tag.Equals("Language", StringComparison.OrdinalIgnoreCase) ||
                            tag.Equals("GameTag",  StringComparison.OrdinalIgnoreCase))
                            continue;

                        string imgPath = Path.Combine(dir, m.Groups[2].Value);
                        if (File.Exists(imgPath))
                            tagToFile[tag] = imgPath;
                        else
                            KupoUIPRPlugin.PluginLog.LogWarning(
                                $"[IconsConfig] Icon file not found: {imgPath} (in {configFile})");
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Error gathering tag mappings: {ex}");
            }
        }

        // ── Phase 2a: build atlas from source files and write cache ───────────

        private static void BuildAtlasAndCache(
            string cacheDir,
            string gameTag,
            Dictionary<string, string> tagToFile)
        {
            var watch = Stopwatch.StartNew();

            // Load raw textures and split by desired filter mode.
            var pointEntries  = new List<(string Tag, Texture2D Tex)>();
            var linearEntries = new List<(string Tag, Texture2D Tex)>();
            var sourceTimestamps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in tagToFile)
            {
                var tex = LoadRawTexture(kv.Value);
                if (tex == null) continue;

                sourceTimestamps[kv.Value] = File.GetLastWriteTimeUtc(kv.Value).Ticks;

                bool usePoint =
                    Textures.TextureResolver.ShouldUsePointFilter(kv.Value) ||
                    tex.width <= 32;

                if (usePoint) pointEntries.Add((kv.Key, tex));
                else          linearEntries.Add((kv.Key, tex));
            }

            var indexEntries = new List<AtlasEntry>();

            if (pointEntries.Count  > 0)
                PackGroup(pointEntries,  FilterMode.Point,    ref _atlasPoint,  indexEntries, isPoint: true);
            if (linearEntries.Count > 0)
                PackGroup(linearEntries, FilterMode.Bilinear, ref _atlasLinear, indexEntries, isPoint: false);

            // Temp individual textures are no longer needed — the atlas owns all pixels.
            foreach (var (_, t) in pointEntries)  UnityEngine.Object.Destroy(t);
            foreach (var (_, t) in linearEntries) UnityEngine.Object.Destroy(t);

            watch.Stop();
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[IconsConfig] Atlas built: {_sprites.Count} icon(s) in {watch.ElapsedMilliseconds} ms. " +
                $"({pointEntries.Count} point-filter, {linearEntries.Count} bilinear)");

            TrySaveCacheToDisk(cacheDir, gameTag, sourceTimestamps, indexEntries);
        }

        private static void PackGroup(
            List<(string Tag, Texture2D Tex)> entries,
            FilterMode filterMode,
            ref Texture2D atlasOut,
            List<AtlasEntry> indexEntries,
            bool isPoint)
        {
            try
            {
                var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = filterMode,
                    wrapMode   = TextureWrapMode.Clamp,
                    hideFlags  = HideFlags.HideAndDontSave,
                };

                var texArray = new Texture2D[entries.Count];
                for (int i = 0; i < entries.Count; i++) texArray[i] = entries[i].Tex;

                // PackTextures returns UV-space rects (0-1). We keep the atlas CPU-readable so
                // we can EncodeToPNG for the disk cache.
                var uvRects = atlas.PackTextures(texArray, padding: 2, maximumAtlasSize: 4096,
                                                 makeNoLongerReadable: false);

                if (uvRects == null || uvRects.Length != entries.Count)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning(
                        $"[IconsConfig] PackTextures returned unexpected rect count " +
                        $"({uvRects?.Length.ToString() ?? "null"} vs {entries.Count}). Falling back to individual sprites.");
                    FallbackToIndividualSprites(entries, filterMode);
                    return;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    var uv = uvRects[i];
                    // Convert UV-space to pixel-space for Sprite.Create.
                    var px = new Rect(
                        Mathf.Round(uv.x      * atlas.width),
                        Mathf.Round(uv.y      * atlas.height),
                        Mathf.Round(uv.width  * atlas.width),
                        Mathf.Round(uv.height * atlas.height));

                    var sprite = Sprite.Create(atlas, px, new Vector2(0.5f, 0.5f), 100f);
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    _sprites[entries[i].Tag] = sprite;

                    indexEntries.Add(new AtlasEntry
                    {
                        Tag     = entries[i].Tag,
                        X       = (int)px.x,
                        Y       = (int)px.y,
                        W       = (int)px.width,
                        H       = (int)px.height,
                        IsPoint = isPoint,
                    });
                }

                atlasOut = atlas;
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError(
                    $"[IconsConfig] PackTextures failed ({ex.GetType().Name}: {ex.Message}). " +
                    $"Falling back to individual sprites (no batching benefit).");
                FallbackToIndividualSprites(entries, filterMode);
            }
        }

        /// <summary>
        /// Fallback: keep individual textures as separate sprites when atlas packing is unavailable.
        /// Sprites still work correctly; they just can't be batched by Unity into a single draw call.
        /// </summary>
        private static void FallbackToIndividualSprites(
            List<(string Tag, Texture2D Tex)> entries,
            FilterMode filterMode)
        {
            foreach (var (tag, tex) in entries)
            {
                try
                {
                    tex.filterMode = filterMode;
                    tex.wrapMode   = TextureWrapMode.Clamp;
                    tex.hideFlags  = HideFlags.HideAndDontSave;
                    var rect   = new Rect(0, 0, tex.width, tex.height);
                    var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
                    sprite.hideFlags   = HideFlags.HideAndDontSave;
                    _sprites[tag]      = sprite;
                }
                catch (Exception ex2)
                {
                    KupoUIPRPlugin.PluginLog.LogError(
                        $"[IconsConfig] Fallback sprite creation failed for '{tag}': {ex2.Message}");
                }
            }
        }

        // ── Phase 2b: write cache to disk ─────────────────────────────────────

        private struct AtlasEntry
        {
            public string Tag;
            public int X, Y, W, H;
            public bool IsPoint;
        }

        private static void TrySaveCacheToDisk(
            string cacheDir,
            string gameTag,
            Dictionary<string, long> sourceTimestamps,
            List<AtlasEntry> indexEntries)
        {
            try
            {
                Directory.CreateDirectory(cacheDir);

                // Save atlas PNGs. Delete stale files when an atlas group is empty.
                string pointPngPath  = Path.Combine(cacheDir, $"icons_atlas_{gameTag}_point.png");
                string linearPngPath = Path.Combine(cacheDir, $"icons_atlas_{gameTag}_linear.png");

                if (_atlasPoint != null)
                    File.WriteAllBytes(pointPngPath, _atlasPoint.EncodeToPNG());
                else if (File.Exists(pointPngPath))
                    File.Delete(pointPngPath);

                if (_atlasLinear != null)
                    File.WriteAllBytes(linearPngPath, _atlasLinear.EncodeToPNG());
                else if (File.Exists(linearPngPath))
                    File.Delete(linearPngPath);

                // Build and save the JSON index.
                var sb = new StringBuilder(1024);
                sb.AppendLine("{");
                sb.AppendLine($"  \"version\": {CacheVersion},");
                sb.AppendLine($"  \"gameTag\": \"{EscapeJson(gameTag)}\",");
                sb.AppendLine("  \"sources\": [");

                int si = 0;
                foreach (var kv in sourceTimestamps)
                {
                    string comma = ++si < sourceTimestamps.Count ? "," : "";
                    sb.AppendLine(
                        $"    {{ \"path\": \"{EscapeJson(kv.Key)}\", \"ticks\": {kv.Value} }}{comma}");
                }

                sb.AppendLine("  ],");
                sb.AppendLine("  \"icons\": [");

                for (int i = 0; i < indexEntries.Count; i++)
                {
                    var  e     = indexEntries[i];
                    string pt  = e.IsPoint ? "true" : "false";
                    string comma = i < indexEntries.Count - 1 ? "," : "";
                    sb.AppendLine(
                        $"    {{ \"tag\": \"{EscapeJson(e.Tag)}\", \"x\": {e.X}, \"y\": {e.Y}, " +
                        $"\"w\": {e.W}, \"h\": {e.H}, \"point\": {pt} }}{comma}");
                }

                sb.AppendLine("  ]");
                sb.Append('}');

                string indexPath = Path.Combine(cacheDir, $"icons_atlas_{gameTag}.json");
                File.WriteAllText(indexPath, sb.ToString(), Encoding.UTF8);

                if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                    KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Atlas cache saved to: {cacheDir}");
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[IconsConfig] Could not save atlas cache: {ex.Message}");
            }
        }

        // ── Phase 2c: load from cache ─────────────────────────────────────────

        private static bool TryLoadFromCache(
            string cacheDir,
            string gameTag,
            Dictionary<string, string> tagToFile)
        {
            try
            {
                string indexPath = Path.Combine(cacheDir, $"icons_atlas_{gameTag}.json");
                if (!File.Exists(indexPath)) return false;

                string json = File.ReadAllText(indexPath, Encoding.UTF8);

                // ── Validate source file timestamps ──────────────────────────
                // Parse cached source records into a path→ticks lookup.
                var cachedSources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in RxCacheSource.Matches(json))
                {
                    if (long.TryParse(m.Groups["t"].Value, out long ticks))
                        cachedSources[UnescapeJson(m.Groups["p"].Value)] = ticks;
                }

                // Entry count must match (catches additions and deletions of icon files).
                if (cachedSources.Count != tagToFile.Count)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        "[IconsConfig] Atlas cache stale (icon count changed). Rebuilding.");
                    return false;
                }

                // Every current source file must be in the cache with the same timestamp.
                foreach (var kv in tagToFile)
                {
                    if (!cachedSources.TryGetValue(kv.Value, out long cachedTicks))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo(
                            $"[IconsConfig] Atlas cache stale (new file: {kv.Value}). Rebuilding.");
                        return false;
                    }

                    long currentTicks = File.GetLastWriteTimeUtc(kv.Value).Ticks;
                    if (currentTicks != cachedTicks)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo(
                            $"[IconsConfig] Atlas cache stale (modified: {kv.Value}). Rebuilding.");
                        return false;
                    }
                }

                // ── Load atlas PNGs ───────────────────────────────────────────
                string pointPngPath  = Path.Combine(cacheDir, $"icons_atlas_{gameTag}_point.png");
                string linearPngPath = Path.Combine(cacheDir, $"icons_atlas_{gameTag}_linear.png");

                if (File.Exists(pointPngPath))
                {
                    _atlasPoint = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode   = TextureWrapMode.Clamp,
                        hideFlags  = HideFlags.HideAndDontSave,
                    };
                    if (!ImageConversion.LoadImage(_atlasPoint, File.ReadAllBytes(pointPngPath)))
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning(
                            "[IconsConfig] Failed to decode cached point atlas. Rebuilding.");
                        DestroyAtlases();
                        return false;
                    }
                }

                if (File.Exists(linearPngPath))
                {
                    _atlasLinear = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode   = TextureWrapMode.Clamp,
                        hideFlags  = HideFlags.HideAndDontSave,
                    };
                    if (!ImageConversion.LoadImage(_atlasLinear, File.ReadAllBytes(linearPngPath)))
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning(
                            "[IconsConfig] Failed to decode cached linear atlas. Rebuilding.");
                        DestroyAtlases();
                        return false;
                    }
                }

                if (_atlasPoint == null && _atlasLinear == null) return false;

                // ── Rebuild sprites from index ────────────────────────────────
                int rebuilt = 0;
                foreach (Match m in RxCacheIcon.Matches(json))
                {
                    string tag  = UnescapeJson(m.Groups["tag"].Value);
                    int    x    = int.Parse(m.Groups["x"].Value);
                    int    y    = int.Parse(m.Groups["y"].Value);
                    int    w    = int.Parse(m.Groups["w"].Value);
                    int    h    = int.Parse(m.Groups["h"].Value);
                    bool   isPt = m.Groups["pt"].Value == "true";

                    var atlas = isPt ? _atlasPoint : _atlasLinear;
                    if (atlas == null) continue;

                    var sprite = Sprite.Create(
                        atlas, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), 100f);
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    _sprites[tag]   = sprite;
                    rebuilt++;
                }

                if (rebuilt == 0)
                {
                    DestroyAtlases();
                    return false;
                }

                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[IconsConfig] Loaded {rebuilt} icon(s) from atlas cache " +
                    $"(skipped {tagToFile.Count} PNG decodes).");
                return true;
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[IconsConfig] Atlas cache load failed ({ex.Message}). Rebuilding.");
                DestroyAtlases();
                _sprites.Clear();
                return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void DestroyAtlases()
        {
            if (_atlasPoint  != null) { UnityEngine.Object.Destroy(_atlasPoint);  _atlasPoint  = null; }
            if (_atlasLinear != null) { UnityEngine.Object.Destroy(_atlasLinear); _atlasLinear = null; }
        }

        /// <summary>Loads a PNG file into a temporary <see cref="Texture2D"/> for atlas packing.</summary>
        private static Texture2D LoadRawTexture(string filePath)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(tex, File.ReadAllBytes(filePath)))
                    return tex;

                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError(
                    $"[IconsConfig] Failed to load raw texture '{filePath}': {ex.Message}");
            }
            return null;
        }

        private static string ReadJsonString(string json, string key)
        {
            var m = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string EscapeJson(string s)
            => (s ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n",  "\\n")
                .Replace("\r",  "\\r");

        private static string UnescapeJson(string s)
            => (s ?? string.Empty)
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\n",  "\n")
                .Replace("\\r",  "\r");
    }
}
