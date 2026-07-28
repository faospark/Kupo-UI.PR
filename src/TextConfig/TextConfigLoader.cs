using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KupoUI.PR.TextConfig
{
    /// <summary>
    /// Discovers and parses all <c>TextConfig.json</c> files under the <c>Modules/</c> folder recursively.
    /// Filters to current active game folder if inside <c>Shared/FF1-FF6</c>.
    /// </summary>
    internal static class TextConfigLoader
    {
        private const string ConfigFileName = "TextConfig.json";
        private static readonly List<TextConfigEntry> _entries = new();

        internal static IReadOnlyList<TextConfigEntry> Entries => _entries;

        internal static void Load(string modulesRootPath)
        {
            _entries.Clear();

            if (!Directory.Exists(modulesRootPath))
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[TextConfig] Modules root not found, skipping: {modulesRootPath}");
                return;
            }

            var gameTag = Textures.TextureResolver.CurrentGameTag;
            var filesToLoad = new List<string>();

            // Find all TextConfig.json files recursively
            var allFiles = Directory.GetFiles(modulesRootPath, ConfigFileName, SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                var normalizedFile = file.Replace('\\', '/');
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

                if (skipFile) continue;

                filesToLoad.Add(file);
            }

            if (filesToLoad.Count == 0)
            {
                KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] No TextConfig.json files found.");
                return;
            }

            var totalLoaded = 0;
            foreach (var file in filesToLoad)
            {
                var loaded = ParseFile(file);
                _entries.AddRange(loaded);
                totalLoaded += loaded.Count;
            }

            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[TextConfig] Loaded {totalLoaded} text rule(s) from {filesToLoad.Count} file(s).");
        }

        private static List<TextConfigEntry> ParseFile(string filePath)
        {
            var result = new List<TextConfigEntry>();
            try
            {
                var json = File.ReadAllText(filePath);

                var gameTag = Textures.TextureResolver.CurrentGameTag;
                var fileGameTag = ReadString(json, "GameTag")?.Trim();
                if (!string.IsNullOrEmpty(fileGameTag) && !fileGameTag.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                // 1. Parse file-level Language scoping if specified
                var fileLanguage = ReadString(json, "Language")?.Trim();

                // 2. Parse key-value dictionary under "texts"
                var textsBlock = ReadSubObject(json, "texts");
                if (textsBlock != null)
                {
                    var matches = Regex.Matches(textsBlock, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
                    foreach (Match match in matches)
                    {
                        var key = match.Groups[1].Value.Trim();
                        var val = match.Groups[2].Value; // Keep formatting/spaces as-is in value
                        val = UnescapeJsonString(val);

                        result.Add(new TextConfigEntry
                        {
                            Key = key,
                            NewText = val,
                            Language = fileLanguage,
                            SourceFile = filePath
                        });
                    }
                }

                // 3. Parse object overrides list under "objects"
                var objectsContent = ExtractArrayContent(json, "objects");
                if (objectsContent != null)
                {
                    var objectBlocks = SplitObjectBlocks(objectsContent);
                    foreach (var block in objectBlocks)
                    {
                        var entry = ParseObjectEntry(block, fileLanguage, filePath);
                        if (entry != null)
                        {
                            result.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[TextConfig] Failed to parse '{filePath}': {ex.Message}");
            }

            return result;
        }

        private static TextConfigEntry ParseObjectEntry(string block, string fileLanguage, string sourceFile)
        {
            var newText = ReadString(block, "NewText");
            if (newText == null)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[TextConfig] Skipping entry without 'NewText' in: {sourceFile}");
                return null;
            }

            return new TextConfigEntry
            {
                TargetObjectName = ReadString(block, "TargetObjectName")?.Trim(),
                TargetPath       = ReadString(block, "TargetPath")?.Trim(),
                SceneName        = ReadString(block, "SceneName")?.Trim(),
                OriginalText     = ReadString(block, "OriginalText"),
                Language         = fileLanguage,
                NewText          = newText,
                SourceFile       = sourceFile
            };
        }

        // ---- Primitive readers ----

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
        }

        private static string ReadString(string json, string key)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            return match.Success ? UnescapeJsonString(match.Groups[1].Value) : null;
        }

        private static string ReadSubObject(string json, string key)
        {
            var keyPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\{{";
            var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return ExtractBalancedBraces(json, match.Index + match.Length - 1);
        }

        private static string ExtractBalancedBraces(string json, int openBraceIndex)
        {
            var depth = 0;
            for (var i = openBraceIndex; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(openBraceIndex, i - openBraceIndex + 1);
                    }
                }
            }

            return null;
        }

        private static string ExtractArrayContent(string json, string arrayKey)
        {
            var keyPattern = $"\"{Regex.Escape(arrayKey)}\"\\s*:\\s*\\[";
            var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var start = match.Index + match.Length - 1;
            var depth = 0;
            for (var i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(start + 1, i - start - 1);
                    }
                }
            }

            return null;
        }

        private static List<string> SplitObjectBlocks(string arrayBody)
        {
            var result = new List<string>();
            var depth  = 0;
            var start  = -1;

            for (var i = 0; i < arrayBody.Length; i++)
            {
                var c = arrayBody[i];
                if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        result.Add(arrayBody.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return result;
        }
    }
}
