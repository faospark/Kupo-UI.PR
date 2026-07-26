using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KupoUI.PR.IconsConfig
{
    internal static class IconsConfigLoader
    {
        private const string ConfigFileName = "IconsConfig.json";
        private static readonly Dictionary<string, Sprite> _sprites = new(StringComparer.OrdinalIgnoreCase);

        internal static void Initialize(string modulesRootPath)
        {
            _sprites.Clear();

            if (!Directory.Exists(modulesRootPath))
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Modules root not found, skipping: {modulesRootPath}");
                return;
            }

            var filesToLoad = new List<string>();
            var gameTag = Textures.TextureResolver.CurrentGameTag;

            try
            {
                // Gather all IconsConfig.json files under Modules/ recursively.
                var allFiles = Directory.GetFiles(modulesRootPath, ConfigFileName, SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    var normalizedFile = file.Replace('\\', '/');

                    // Apply game-tag filtering for files inside any FFx/ sub-folders anywhere in the path.
                    var pathSegments = normalizedFile.Split('/');
                    var skipFile = false;

                    foreach (var segment in pathSegments)
                    {
                        if (segment.StartsWith("block", StringComparison.OrdinalIgnoreCase))
                        {
                            skipFile = true;
                            break;
                        }

                        var isGameTagFolder =
                            segment.Equals("FF1", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("FF2", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("FF3", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("FF4", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("FF5", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("FF6", StringComparison.OrdinalIgnoreCase);

                        if (isGameTagFolder && !segment.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                        {
                            skipFile = true;
                            break;
                        }
                    }

                    if (skipFile)
                    {
                        continue;
                    }

                    filesToLoad.Add(file);
                }

                // Load each config file additively
                int loadedIconsCount = 0;
                foreach (var file in filesToLoad)
                {
                    string json = File.ReadAllText(file);
                    // Strip single-line comments
                    json = Regex.Replace(json, @"//.*", "");

                    var dir = Path.GetDirectoryName(file);

                    // Regex to parse flat JSON key-value string pairs
                    var matches = Regex.Matches(json, @"""([^""]+)""\s*:\s*""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        string tag = match.Groups[1].Value;
                        string relativePath = match.Groups[2].Value;

                        string spritePath = Path.Combine(dir, relativePath);
                        if (File.Exists(spritePath))
                        {
                            var sprite = LoadSprite(spritePath);
                            if (sprite != null)
                            {
                                _sprites[tag] = sprite;
                                loadedIconsCount++;
                            }
                        }
                        else
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning($"[IconsConfig] Icon file not found: {spritePath} (referenced in {file})");
                        }
                    }
                }

                if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                    KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Loaded {loadedIconsCount} custom icon(s) from {filesToLoad.Count} config file(s).");
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Error initializing icons config: {ex}");
            }
        }

        internal static bool HasSprite(string tag)
        {
            return !string.IsNullOrEmpty(tag) && _sprites.ContainsKey(tag);
        }

        internal static Sprite GetSprite(string tag)
        {
            if (_sprites.TryGetValue(tag, out var sprite))
            {
                return sprite;
            }
            if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                KupoUIPRPlugin.PluginLog.LogWarning($"[IconsConfig] Sprite lookup failed for tag: '{tag}'. Registered keys: {string.Join(", ", _sprites.Keys)}");
            return null;
        }

        private static Sprite LoadSprite(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                
                if (ImageConversion.LoadImage(texture, fileData))
                {
                    bool usePoint = Textures.TextureResolver.ShouldUsePointFilter(filePath) || texture.width <= 32;
                    texture.filterMode = usePoint ? FilterMode.Point : FilterMode.Bilinear;
                    Rect rect = new Rect(0.0f, 0.0f, texture.width, texture.height);
                    Vector2 pivot = new Vector2(0.5f, 0.5f);
                    
                    Sprite sprite = Sprite.Create(texture, rect, pivot, 100.0f);
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    return sprite;
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Failed to load texture from '{filePath}': {ex}");
            }
            return null;
        }
    }
}
