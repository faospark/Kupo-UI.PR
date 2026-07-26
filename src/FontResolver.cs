using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Last.Management;
using UnityEngine;

namespace KupoUI.PR
{
    internal class FontConfigEntry
    {
        public string FontName { get; set; } = "";
        public float? LineSpace { get; set; }
        public float? YOffset { get; set; }
    }

    internal static class FontResolver
    {
        private static string _modulesRootPath;

        internal static Dictionary<(FontManager.FontType, Last.Data.Parameters.Language?), FontConfigEntry> FontConfigMapping { get; } = new();
        internal static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, string> FontParameterLanguages { get; } = new();
        internal static Dictionary<string, UnityEngine.Font> LoadedFonts { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, float> SwappedFontYOffsets { get; } = new();

        internal static void Initialize(string modulesRootPath)
        {
            _modulesRootPath = modulesRootPath;
            LoadFontConfig();
        }

        private static FontConfigEntry ParseFontConfigEntry(string json, string keyName)
        {
            // Extract balanced braces for the object block (e.g. "Font01": { ... })
            var objStr = KupoUIPRPlugin.ReadSubObject(json, keyName);
            if (objStr != null)
            {
                var nameMatch = Regex.Match(objStr, "\"FontName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var spaceMatch = Regex.Match(objStr, "\"LineSpace\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
                var yOffsetMatch = Regex.Match(objStr, "\"YOffset\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);

                var fontName = nameMatch.Success ? nameMatch.Groups[1].Value : "";

                float? space = null;
                if (spaceMatch.Success && float.TryParse(spaceMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedSpace))
                {
                    space = parsedSpace;
                }

                float? yOffset = null;
                if (yOffsetMatch.Success && float.TryParse(yOffsetMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedYOffset))
                {
                    yOffset = parsedYOffset;
                }

                if (!string.IsNullOrEmpty(fontName))
                {
                    return new FontConfigEntry
                    {
                        FontName = fontName,
                        LineSpace = space,
                        YOffset = yOffset
                    };
                }
            }
            else
            {
                // Match string: "KeyName" : "Consolas"
                var strMatch = Regex.Match(json, $"\"{keyName}\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                if (strMatch.Success)
                {
                    var value = strMatch.Groups[1].Value;
                    if (!string.IsNullOrEmpty(value))
                    {
                        return new FontConfigEntry
                        {
                            FontName = value
                        };
                    }
                }
            }
            return null;
        }

        internal static bool TryGetFontConfig(FontManager.FontType type, string languageStr, out FontConfigEntry entry)
        {
            Last.Data.Parameters.Language? lang = null;
            if (!string.IsNullOrEmpty(languageStr) && Enum.TryParse<Last.Data.Parameters.Language>(languageStr, out var parsedLang))
            {
                lang = parsedLang;
            }

            // 1. Try specific FontType + specific Language (e.g. Font01_Ja)
            if (lang.HasValue && FontConfigMapping.TryGetValue((type, lang.Value), out entry))
            {
                return true;
            }

            // 2. Try specific FontType + no Language (e.g. Font01)
            if (FontConfigMapping.TryGetValue((type, null), out entry))
            {
                return true;
            }

            entry = null;
            return false;
        }

        private static void LoadFontConfig()
        {
            FontConfigMapping.Clear();
            LoadedFonts.Clear();
            SwappedFontYOffsets.Clear();

            var fontsDir = Path.Combine(_modulesRootPath, "Shared");
            var configPath = Path.Combine(fontsDir, "fontconfig.json");

            if (!Directory.Exists(fontsDir))
            {
                try
                {
                    Directory.CreateDirectory(fontsDir);
                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Created directory: {fontsDir}");
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to create directory '{fontsDir}': {ex}");
                }
            }

            var templateJson =
@"{
  ""En"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.66 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Ja"": {
    ""Font01"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.73 },
    ""Font02"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.66 },
    ""Font03"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.67 },
    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.66 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Fr"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.66 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""De"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.66 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""It"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.66 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Ru"": {
    ""Font01"": { ""FontName"": ""ITCAvantGardeW1G-Medium"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Pt"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""SE-ALPSCB__"", ""LineSpace"": 1.0 },
    ""Font03"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.66 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"", ""LineSpace"": 0.66 },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"", ""LineSpace"": 0.73 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Th"": {
    ""Font01"": { ""FontName"": ""arnewhebesans-th_rg"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""arnewhebesans-th_rg"", ""LineSpace"": 1.0 },
    ""Font03"": { ""FontName"": ""arnewhebesans-th_rg"", ""LineSpace"": 1.0 },
    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.66 },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"", ""LineSpace"": 0.66 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Ko"": {
    ""Font01"": { ""FontName"": ""FOTK-YoonGothic750"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Zht"": {
    ""Font01"": { ""FontName"": ""arudjingxiheiu30_db"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  },
  ""Zhc"": {
    ""Font01"": { ""FontName"": ""arudjingxiheig30_db"", ""LineSpace"": 1.0 },
    ""Font02"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font03"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font04"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font05"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"", ""LineSpace"": 0.6 },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"", ""LineSpace"": 1.0 },
    ""Font08"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font09"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Font10"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 },
    ""Default"": { ""FontName"": ""Arial"", ""LineSpace"": 1.2 }
  }
}";

            var helpPath = Path.Combine(fontsDir, "font-help.txt");
            try
            {
                var helpText =
@"KupoUI.PR Font Swap Help Guide
=============================

This directory manages custom font swapping for KupoUI.PR.

Files:
- fontconfig.json: Holds active font configurations. Copy target blocks or specific keys from the baseline template below here to customize them.
- font-help.txt: This help file (contains baseline default values at the bottom).

How to Customize:
1. Reference the game's default baseline font names at the bottom of this file.
2. Identify the language block (e.g. ""En"", ""Ja"", ""Th"", etc.) and the specific FontType (Font01..Font10) you wish to change. Note that font enums differ per language.
3. In fontconfig.json, create the corresponding language block (ensure it matches the language you are playing in-game) and intentionally define the specific FontType key you want to change.
4. Edit the configuration block:
   - Set ""FontName"" to the desired system font family name (e.g. ""Segoe UI"").
   - Adjust ""LineSpace"" (decimal factor, e.g. 0.85) if needed.
   - Adjust ""YOffset"" (floating point pixels, e.g. 2.0 to move text up, -1.5 to move down) if needed.
5. Restart the game to apply changes.

Understanding Language Blocks 
{
  ""En"": {
    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" },
    ""Font02"": { ""FontName"": ""Arial"" },
    ""Font03"": { ""FontName"": ""Arial"" },
    ""Font04"": { ""FontName"": ""Arial"" },
    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" },
    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" },
    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" },
    ""Font08"": { ""FontName"": ""sqex-MonoSix"" },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" },
    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }
  }
}
Each language has its own set of FontTypes and implementations. Seeing FOT-NewRodinPro-DB for numbers 
in one language does not mean it is used everywhere. 
Example:
Modern Font (English)
- Default: SE-ALPSTN__
- To change it, update both Font01 and Font07

Classic Font (English)
- Default: sqex-MonoSix
- To change it, update both Font08 and Font10

Menu Numbers Font Type Pairing in some instances
- FOT-NewRodinPro-DB → Modern English (Font05)
- PIXELREMASTERFONT → Classic English (Font09)

ALL Arial values are suggested to be replaced with your Ideal Font choice 
as Arial is declared multiple but is not bundled with the game at all (unlike the default fonts in general). 

Not every font FONT* has to be edited
Example fontconfig.json (Limited Scope Override):
{
  ""En"": {
    ""Font01"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.85  },
    ""Font07"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.85  }
  }
}
The example above replaced the horrible font used for the English langauge of the game. 

Supported Languages:
- En (English)
- Ja (Japanese)
- Fr (French)
- De (German)
- It (Italian)
- Ru (Russian)
- Pt (Portuguese)
- Th (Thai)
- Ko (Korean)
- Zht (Traditional Chinese)
- Zhc (Simplified Chinese)

================================================================================
BASELINE TEMPLATE DEFAULT VALUES (Copy keys/blocks from here into fontconfig.json):
================================================================================
" + templateJson;

                File.WriteAllText(helpPath, helpText);
                KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Generated/Updated help guide at {helpPath}");
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to generate font-help.txt: {ex}");
            }

            if (!File.Exists(configPath))
            {
                try
                {
                    var minimalConfigJson =
@"{" + "\n" +
@"  ""NOTE"": ""To customize fonts, define desired language blocks or font keys here. See font-help.txt for all baseline default values.""," + "\n" +
@"  ""En"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.90, ""YOffset"": 4 }," + "\n" +
@"    ""Font07"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.90, ""YOffset"": 4 }" + "\n" +
@"  }" + "\n" +
@"}";
                    File.WriteAllText(configPath, minimalConfigJson);
                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Generated default minimal fontconfig.json at {configPath}");
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to generate template fontconfig.json: {ex}");
                }
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);

                // 1. Detect root-level Language parameter (e.g. "Language": "Pt")
                Last.Data.Parameters.Language? fileLanguage = null;
                var langPropMatch = Regex.Match(json, "\"Language\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (langPropMatch.Success)
                {
                    var langStr = langPropMatch.Groups[1].Value;
                    if (Enum.TryParse<Last.Data.Parameters.Language>(langStr, true, out var parsedLang))
                    {
                        fileLanguage = parsedLang;
                        KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Root language target detected: {fileLanguage}");
                    }
                }

                // Helper to register parsed configs
                void AddConfig(FontManager.FontType fontType, Last.Data.Parameters.Language? lang, FontConfigEntry entry, string sourceContext)
                {
                    FontConfigMapping[(fontType, lang)] = entry;
                    var langStr = lang.HasValue ? lang.Value.ToString() : "Global";
                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Loaded config ({langStr}) via {sourceContext}: {fontType} -> name='{entry.FontName}' (LineSpace={entry.LineSpace})");
                }

                // Cache enum arrays once
                var allLanguages = (Last.Data.Parameters.Language[])Enum.GetValues(typeof(Last.Data.Parameters.Language));
                var allFontTypes = (FontManager.FontType[])Enum.GetValues(typeof(FontManager.FontType));

                // 2. Parse language-specific nested blocks (e.g. "Pt": { ... })
                foreach (var lang in allLanguages)
                {
                    var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                    if (string.IsNullOrEmpty(langName)) continue;

                    var langBlock = KupoUIPRPlugin.ReadSubObject(json, langName);
                    if (langBlock == null) continue;

                    // Parse specific FontTypes within the language block
                    foreach (var fontType in allFontTypes)
                    {
                        var fontTypeName = Enum.GetName(typeof(FontManager.FontType), fontType);
                        if (string.IsNullOrEmpty(fontTypeName)) continue;

                        var entry = ParseFontConfigEntry(langBlock, fontTypeName);
                        if (entry != null)
                        {
                            AddConfig(fontType, lang, entry, $"nested block '{langName}'");
                        }
                    }
                }

                // 3. Parse root-level configs
                var removalRanges = new List<(int index, int length)>();
                foreach (var lang in allLanguages)
                {
                    var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                    if (string.IsNullOrEmpty(langName)) continue;

                    var keyPattern = $"\"{Regex.Escape(langName)}\"\\s*:\\s*\\{{";
                    var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
                    if (!match.Success) continue;

                    var block = KupoUIPRPlugin.ExtractBalancedBraces(json, match.Index + match.Length - 1);
                    if (block == null) continue;

                    removalRanges.Add((match.Index, match.Index + match.Length - 1 + block.Length - match.Index));
                }

                removalRanges.Sort((a, b) => b.index.CompareTo(a.index));
                var rootJsonSb = new System.Text.StringBuilder(json);
                foreach (var (idx, len) in removalRanges)
                    rootJsonSb.Remove(idx, len);
                var rootJson = rootJsonSb.ToString();

                // Root FontType keys
                foreach (var fontType in allFontTypes)
                {
                    var fontTypeName = Enum.GetName(typeof(FontManager.FontType), fontType);
                    if (string.IsNullOrEmpty(fontTypeName)) continue;

                    // Load root FontType (e.g., "Font01")
                    var baseEntry = ParseFontConfigEntry(rootJson, fontTypeName);
                    if (baseEntry != null)
                    {
                        AddConfig(fontType, fileLanguage, baseEntry, "root");
                    }

                    // Load root suffix FontType (e.g., "Font01_Ja")
                    foreach (var lang in allLanguages)
                    {
                        var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                        if (string.IsNullOrEmpty(langName)) continue;

                        var langKey = $"{fontTypeName}_{langName}";
                        var langEntry = ParseFontConfigEntry(rootJson, langKey);
                        if (langEntry != null)
                        {
                            AddConfig(fontType, lang, langEntry, "root suffix");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to load fontconfig.json: {ex}");
            }
        }
    }
}
