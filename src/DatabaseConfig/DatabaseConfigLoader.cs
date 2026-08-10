using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KupoUI.PR.DatabaseConfig
{
    internal static class DatabaseConfigLoader
    {
        private const string ConfigFileName = "DatabaseConfig.json";
        private static readonly List<DatabaseConfigEntry> _entries = new();

        internal static IReadOnlyList<DatabaseConfigEntry> Entries => _entries;

        internal static void Load(string modulesRootPath)
        {
            _entries.Clear();

            if (!Directory.Exists(modulesRootPath))
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[DatabaseConfig] Modules root not found, skipping: {modulesRootPath}");
                return;
            }

            var filesToLoad = new List<string>();

            // Find all DatabaseConfig.json files recursively
            var allFiles = Directory.GetFiles(modulesRootPath, ConfigFileName, SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                if (ModulePathFilter.ShouldSkipConfigFile(file, modulesRootPath))
                {
                    continue;
                }

                filesToLoad.Add(file);
            }

            if (filesToLoad.Count == 0)
            {
                KupoUIPRPlugin.PluginLog.LogInfo("[DatabaseConfig] No DatabaseConfig.json files found.");
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
                $"[DatabaseConfig] Loaded {totalLoaded} database rule(s) from {filesToLoad.Count} file(s).");
        }

        private static List<DatabaseConfigEntry> ParseFile(string filePath)
        {
            var result = new List<DatabaseConfigEntry>();
            try
            {
                var json = File.ReadAllText(filePath);

                var gameTag = Textures.TextureResolver.CurrentGameTag;
                var fileGameTag = ReadString(json, "GameTag")?.Trim();
                if (!string.IsNullOrEmpty(fileGameTag) && !fileGameTag.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                // Parse MonsterParty array content
                var arrayContent = ExtractArrayContent(json, "MonsterParty");
                if (arrayContent != null)
                {
                    var blocks = SplitObjectBlocks(arrayContent);
                    foreach (var block in blocks)
                    {
                        var entry = ParseMonsterPartyEntry(block, filePath);
                        if (entry != null)
                        {
                            result.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[DatabaseConfig] Failed to parse '{filePath}': {ex.Message}");
            }

            return result;
        }

        private static DatabaseConfigEntry ParseMonsterPartyEntry(string block, string sourceFile)
        {
            var id = ReadInt(block, "id");
            if (id == null)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[DatabaseConfig] Skipping entry without 'id' in: {sourceFile}");
                return null;
            }

            var entry = new DatabaseConfigEntry
            {
                Id = id.Value,
                SourceFile = sourceFile,
                BattleBackgroundAssetId = ReadInt(block, "battle_background_asset_id"),
                BattleBgmAssetId = ReadInt(block, "battle_bgm_asset_id"),
                AppearanceProduction = ReadInt(block, "appearance_production"),
                ScriptNameId = ReadInt(block, "script_name") ?? ReadInt(block, "script_name_id"),
                BattlePattern1 = ReadInt(block, "battle_pattern1"),
                BattlePattern2 = ReadInt(block, "battle_pattern2"),
                BattlePattern3 = ReadInt(block, "battle_pattern3"),
                BattlePattern4 = ReadInt(block, "battle_pattern4"),
                BattlePattern5 = ReadInt(block, "battle_pattern5"),
                BattlePattern6 = ReadInt(block, "battle_pattern6"),
                NotEscape = ReadInt(block, "not_escape"),
                BattleFlagGroupId = ReadInt(block, "battle_flag_group_id"),
                GetValue = ReadInt(block, "get_value"),
                GetAp = ReadInt(block, "get_ap"),

                Monster1 = ReadInt(block, "monster1"),
                Monster1XPosition = ReadInt(block, "monster1_x_position"),
                Monster1YPosition = ReadInt(block, "monster1_y_position"),
                Monster1Group = ReadInt(block, "monster1_group"),

                Monster2 = ReadInt(block, "monster2"),
                Monster2XPosition = ReadInt(block, "monster2_x_position"),
                Monster2YPosition = ReadInt(block, "monster2_y_position"),
                Monster2Group = ReadInt(block, "monster2_group"),

                Monster3 = ReadInt(block, "monster3"),
                Monster3XPosition = ReadInt(block, "monster3_x_position"),
                Monster3YPosition = ReadInt(block, "monster3_y_position"),
                Monster3Group = ReadInt(block, "monster3_group"),

                Monster4 = ReadInt(block, "monster4"),
                Monster4XPosition = ReadInt(block, "monster4_x_position"),
                Monster4YPosition = ReadInt(block, "monster4_y_position"),
                Monster4Group = ReadInt(block, "monster4_group"),

                Monster5 = ReadInt(block, "monster5"),
                Monster5XPosition = ReadInt(block, "monster5_x_position"),
                Monster5YPosition = ReadInt(block, "monster5_y_position"),
                Monster5Group = ReadInt(block, "monster5_group"),

                Monster6 = ReadInt(block, "monster6"),
                Monster6XPosition = ReadInt(block, "monster6_x_position"),
                Monster6YPosition = ReadInt(block, "monster6_y_position"),
                Monster6Group = ReadInt(block, "monster6_group"),

                Monster7 = ReadInt(block, "monster7"),
                Monster7XPosition = ReadInt(block, "monster7_x_position"),
                Monster7YPosition = ReadInt(block, "monster7_y_position"),
                Monster7Group = ReadInt(block, "monster7_group"),

                Monster8 = ReadInt(block, "monster8"),
                Monster8XPosition = ReadInt(block, "monster8_x_position"),
                Monster8YPosition = ReadInt(block, "monster8_y_position"),
                Monster8Group = ReadInt(block, "monster8_group"),

                Monster9 = ReadInt(block, "monster9"),
                Monster9XPosition = ReadInt(block, "monster9_x_position"),
                Monster9YPosition = ReadInt(block, "monster9_y_position"),
                Monster9Group = ReadInt(block, "monster9_group"),
            };

            return entry;
        }

        // ---- Primitive readers ----

        private static string ReadString(string json, string key)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int? ReadInt(string json, string key)
        {
            var match = Regex.Match(
                json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"?(-?\\d+)\"?",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, out var v) ? v : (int?)null;
        }

        private static string ExtractArrayContent(string json, string arrayKey)
        {
            var keyPattern = $"\"{Regex.Escape(arrayKey)}\"\\s*:\\s*\\[";
            var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var start = match.Index + match.Length - 1; // position of '['
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

            return null; // Malformed JSON
        }

        private static List<string> SplitObjectBlocks(string arrayBody)
        {
            var result = new List<string>();
            var depth = 0;
            var start = -1;

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
