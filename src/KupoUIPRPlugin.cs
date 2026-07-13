using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using KupoUI.PR.Compatibility;
using KupoUI.PR.ObjectConfig;
using KupoUI.PR.Patches;
using KupoUI.PR.Textures;

namespace KupoUI.PR;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class KupoUIPRPlugin : BasePlugin
{
    public const string PluginGuid = "faospark.kupoui.pr";
    public const string PluginName = "KupoUI.PR";
    public const string PluginVersion = "1.0.0";
    private const string TextureRootFolder = "Modules";
    internal static string ModulesRootPath { get; private set; }

    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static ConfigEntry<bool> DisableMouseCursorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ForceVSyncConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SaveHighlightColorConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UiFramesFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UIThemesFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UIBgColorFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> CursorsFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> ButtonPromptsFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> DiagnosticTextureLoggerConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ScaledDownMenuConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TitleScreenBgColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> MessageSpeakerPrefixConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> SpeakerNameUppercaseConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> HideSpeakerTagConfig { get; private set; } = null!;
    internal static ConfigEntry<string> DialogueFontSizeConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticMessageSpeakerPrefixLoggingConfig { get; private set; } = null!;
    internal static bool IsTextureLoggerEnabled { get; private set; }

    internal static ConfigEntry<bool> EnableSpeakerPortraitsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticPortraitLoggingConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> FlipSpeakerPortraitsConfig { get; private set; } = null!;

    /// <summary>
    /// Speaker ID → display name registrations loaded from the "speakers" block of speaker-names.json.
    /// Always applied when the speaker ID matches — not limited to blank-name fallback.
    /// </summary>
    internal static Dictionary<string, string> SpeakerNamesOverride { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dialogue message key → (speakerId override, speakerName override) loaded from the "messageOverrides" block.
    /// Takes highest priority — overrides both the game's speaker ID and name for a specific dialogue line.
    /// </summary>
    internal static Dictionary<string, (string SpeakerId, string SpeakerName)> MessageSpeakerOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal static ConfigEntry<bool> FontSwapEnabledConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticsLogFontMappingConfig { get; private set; } = null!;

    internal class FontConfigEntry
    {
        public string FontFile { get; set; } = "";
        public string FontName { get; set; } = "";
        public float? LineSpace { get; set; }
        public int? FontSize { get; set; }
    }
    internal static Dictionary<(Last.Management.FontManager.FontType, Last.Data.Parameters.Language?), FontConfigEntry> FontConfigMapping { get; } = new();
    internal static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, string> FontParameterLanguages { get; } = new();
    internal static Dictionary<string, UnityEngine.Font> LoadedFonts { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Windows GDI P/Invokes to register font files temporarily for our process
    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private const uint FR_PRIVATE = 0x10;
    private static readonly HashSet<string> RegisteredFontFiles = new(StringComparer.OrdinalIgnoreCase);

    public override void Load()
    {
        PluginLog = Log;

        FontSwapEnabledConfig = Config.Bind(
            "FontSwap",
            "Enabled",
            false,
            "If true, swaps default game fonts with custom font files defined in Modules/Shared/Fonts/fontconfig.json.");

        SaveHighlightColorConfig = Config.Bind(
            "UI",
            "SaveHighlightColor",
            "Disable",
            "Customize Quick Save and Auto Save highlight color. Options: Original, DarkNavy, DarkGreen, DarkViolet, DarkYellow, DarkOrange, Disable.");

        ScaledDownMenuConfig = Config.Bind(
            "UI",
            "ScaledDownMenu",
            true,
            "Shrinks the entire in-game menu by 10%");

        TitleScreenBgColorConfig = Config.Bind(
            "UI",
            "TitleScreenBgColor",
            "original",
            "Color for the title screen background. Options: original, white, black, navy, crimson, violet.");

        DialogueFontSizeConfig = Config.Bind(
            "UI-Dialog",
            "DialogueFontSize",
            "36",
            "Font size to use for Dialogue Text UI. Default is 36. This value can scale up to 48-ish; you can even set to Auto to use the font's declared size in game");

        MessageSpeakerPrefixConfig = Config.Bind(
            "UI-Dialog",
            "MessageSpeakerPrefix",
            true,
            "If true, adds a prefix to the message window speaker text to display the speaker name wihout altering the game files. Alternativ to Classic Text Box Framework");

        SpeakerNameUppercaseConfig = Config.Bind(
            "UI-Dialog",
            "SpeakerNameUppercase",
            false,
            "If true, transforms the speaker name to UPPERCASE before prepending it to the dialogue message.");

        HideSpeakerTagConfig = Config.Bind(
            "UI-Dialog",
            "HideSpeakerTag",
            true,
            "If true, hides the speaker name tag bubble by moving it off-screen. Will conflict with older mods that uses the box as portraits");

        EnableSpeakerPortraitsConfig = Config.Bind(
            "UI-Dialog",
            "EnableSpeakerPortraits",
            true,
            "If true, dynamically injects speaker portraits during dialogue sequences.");

        FlipSpeakerPortraitsConfig = Config.Bind(
            "UI-Dialog",
            "FlipSpeakerPortraits",
            true,
            "If true, flips all speaker portraits horizontally.");

        UIThemesFolderConfig = Config.Bind(
            "UI and Customizations",
            "UIThemesFolder",
            "",
            "Specify the folder name under {GameRoot}/Modules/01-UI-Themes for UI theme overrides.");

        UiFramesFolderConfig = Config.Bind(
            "UI and Customizations",
            "UiFramesFolder",
            "",
            "Specify the folder name under {GameRoot}/Modules/02-UI-Frames for UI frame overrides.");

        UIBgColorFolderConfig = Config.Bind(
            "UI and Customizations",
            "UIBgColorFolder",
            "",
            "Specify the folder name under {GameRoot}/Modules/03-UI-BgColor for UI background overrides.");

        CursorsFolderConfig = Config.Bind(
            "UI and Customizations",
            "CursorsFolder",
            "",
            "Specify the folder name under {GameRoot}/Modules/04-UI-Cursors for Cursor overrides.");

        ButtonPromptsFolderConfig = Config.Bind(
            "UI and Customizations",
            "ButtonPromptsFolder",
            "",
            "Specify the folder name under {GameRoot}/Modules/05-Button-Prompts for Button prompt overrides.");

        DiagnosticsLogFontMappingConfig = Config.Bind(
            "Z - Diagnostics",
            "LogFontMapping",
            false,
            "If true, logs information about FontManager.CreateFontParameter and set_FontInstance requests to identify FontType mappings."
        );

        DiagnosticMessageSpeakerPrefixLoggingConfig = Config.Bind(
            "Z - Diagnostics",
            "MessageSpeakerPrefixLogging",
            false,
            "If true, logs speaker name replacements.");

        DiagnosticTextureLoggerConfig = Config.Bind(
            "Z - Diagnostics",
            "TextureLogger",
            "Off",
            "Texture Resolution Logger mode: Off, Discoveries, Resolutions, Misses, All");

        DiagnosticPortraitLoggingConfig = Config.Bind(
            "Z - Diagnostics",
            "PortraitLogging",
            true,
            "If true, outputs debug information for portrait lifecycle and resolution.");

        DisableMouseCursorConfig = Config.Bind(
            "Utility",
            "DisableMouseCursor",
            false,
            "If true, disables the default mouse cursor inside game frame.");

        ForceVSyncConfig = Config.Bind(
            "Utility",
            "ForceVSync",
            false,
            "If true, forces VSync on startup.");

        EnableTextureHotReloadConfig = Config.Bind(
            "Utility",
            "EnableTextureHotReload",
            false,
            "If true, watches texture folders and reloads index when files change.");

        TextureHotReloadDebounceMsConfig = Config.Bind(
            "Utility",
            "TextureHotReloadDebounceMs",
            350,
            "Debounce window in milliseconds before rebuilding texture index after file changes.");

        EnableDDSTexturesConfig = Config.Bind(
            "Utility",
            "EnableDDSTextures",
            true,
            "Experimental - If true, enables loading DDS textures (DXT1/DXT5 and uncompressed RGBA32).");

        var (loggerEnabled, logDiscoveries, logResolutions, logMisses) = ResolveDiagnosticTextureLoggerConfig(DiagnosticTextureLoggerConfig.Value);

        IsTextureLoggerEnabled = loggerEnabled;

        TextureLogger.Initialize(
            loggerEnabled,
            logDiscoveries,
            logResolutions,
            logMisses);

        ModulesRootPath = System.IO.Path.Combine(Paths.GameRootPath, TextureRootFolder);

        LoadFontConfig();

        TextureResolver.Initialize(
            TextureRootFolder,
            UiFramesFolderConfig.Value,
            UIThemesFolderConfig.Value,
            UIBgColorFolderConfig.Value,
            CursorsFolderConfig.Value,
            ButtonPromptsFolderConfig.Value);

        ExternalModDetector.LogLoadedOptionalMods(Log);

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        ForceVSyncPatch.ApplyNow();
        ObjectConfigPatch.Initialize(ModulesRootPath);

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        Log.LogInfo($"DisableMouseCursor = {DisableMouseCursorConfig.Value}");
        Log.LogInfo($"ForceVSync = {ForceVSyncConfig.Value}");
        Log.LogInfo($"SaveHighlightColor = {SaveHighlightColorConfig.Value}");
        Log.LogInfo($"EnableCustomTextures = {EnableCustomTextures}");
        Log.LogInfo($"ScaledDownMenu = {ScaledDownMenuConfig.Value}");
        Log.LogInfo($"TitleScreenBgColor = {TitleScreenBgColorConfig.Value}");
        Log.LogInfo($"MessageSpeakerPrefix = {MessageSpeakerPrefixConfig.Value}");
        Log.LogInfo($"SpeakerNameUppercase = {SpeakerNameUppercaseConfig.Value}");
        Log.LogInfo($"HideSpeakerTag = {HideSpeakerTagConfig.Value}");
        Log.LogInfo($"DialogueFontSize = {DialogueFontSizeConfig.Value}");
        Log.LogInfo($"MessageSpeakerPrefixLogging = {DiagnosticMessageSpeakerPrefixLoggingConfig.Value}");
        Log.LogInfo($"FontSwapEnabled = {FontSwapEnabledConfig.Value}");
        Log.LogInfo($"DiagnosticsLogFontMapping = {DiagnosticsLogFontMappingConfig.Value}");

        Log.LogInfo($"EnableSpeakerPortraits = {EnableSpeakerPortraitsConfig.Value}");
        Log.LogInfo($"PortraitLogging = {DiagnosticPortraitLoggingConfig.Value}");

        LoadSpeakerNames();
    }

    public void OnDestroy()
    {
        foreach (var path in RegisteredFontFiles)
        {
            try
            {
                RemoveFontResourceEx(path, FR_PRIVATE, IntPtr.Zero);
                PluginLog.LogInfo($"[FontSwap] Unregistered font file: {Path.GetFileName(path)}");
            }
            catch { }
        }
        RegisteredFontFiles.Clear();
    }

    private static (bool enabled, bool logDiscoveries, bool logResolutions, bool logMisses) ResolveDiagnosticTextureLoggerConfig(string configValue)
    {
        if (string.IsNullOrWhiteSpace(configValue))
        {
            return (false, false, false, false);
        }

        var lower = configValue.ToLowerInvariant();
        if (lower == "all")
        {
            return (true, true, true, true);
        }
        var enabled = lower != "off" && lower != "false" && lower != "0";
        var logDiscoveries = lower.Contains("discover");
        var logResolutions = lower.Contains("resol");
        var logMisses = lower.Contains("miss");

        return (enabled, logDiscoveries, logResolutions, logMisses);
    }

    private void RegisterFontFile(string fontFile)
    {
        var fontFilePath = Path.Combine(ModulesRootPath, "Shared", "Fonts", fontFile);
        if (File.Exists(fontFilePath))
        {
            var normalizedPath = Path.GetFullPath(fontFilePath);
            if (!RegisteredFontFiles.Contains(normalizedPath))
            {
                try
                {
                    int result = AddFontResourceEx(normalizedPath, FR_PRIVATE, IntPtr.Zero);
                    if (result > 0)
                    {
                        RegisteredFontFiles.Add(normalizedPath);
                        PluginLog.LogInfo($"[FontSwap] Registered font file with OS: '{fontFile}' ({result} font(s) added)");
                    }
                    else
                    {
                        PluginLog.LogWarning($"[FontSwap] AddFontResourceEx returned 0 for '{fontFile}'. LastError={Marshal.GetLastWin32Error()}");
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.LogError($"[FontSwap] Failed to register font file '{fontFile}': {ex}");
                }
            }
        }
        else
        {
            PluginLog.LogWarning($"[FontSwap] Font file not found at: {fontFilePath}");
        }
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

    private static string ReadSubObject(string json, string key)
    {
        var keyPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\{{";
        var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        return ExtractBalancedBraces(json, match.Index + match.Length - 1);
    }

    private FontConfigEntry ParseFontConfigEntry(string json, string keyName)
    {
        // Extract balanced braces for the object block (e.g. "Font01": { ... })
        var objStr = ReadSubObject(json, keyName);
        if (objStr != null)
        {
            var fileMatch = Regex.Match(objStr, "\"FontFile\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var nameMatch = Regex.Match(objStr, "\"FontName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var spaceMatch = Regex.Match(objStr, "\"LineSpace\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            var sizeMatch = Regex.Match(objStr, "\"FontSize\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);

            var file = fileMatch.Success ? fileMatch.Groups[1].Value : "";
            var fontName = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(file);

            float? space = null;
            if (spaceMatch.Success && float.TryParse(spaceMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedSpace))
            {
                space = parsedSpace;
            }
            int? size = null;
            if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var parsedSize))
            {
                size = parsedSize;
            }

            if (!string.IsNullOrEmpty(file))
            {
                RegisterFontFile(file);
            }

            if (!string.IsNullOrEmpty(fontName))
            {
                return new FontConfigEntry
                {
                    FontFile = file,
                    FontName = fontName,
                    LineSpace = space,
                    FontSize = size
                };
            }
        }
        else
        {
            // Match string: "KeyName" : "MyFont.ttf" or "KeyName" : "Consolas"
            var strMatch = Regex.Match(json, $"\"{keyName}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (strMatch.Success)
            {
                var value = strMatch.Groups[1].Value;
                if (!string.IsNullOrEmpty(value))
                {
                    var hasExtension = value.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                       value.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);

                    string file = "";
                    string fontName = value;

                    if (hasExtension)
                    {
                        file = value;
                        fontName = Path.GetFileNameWithoutExtension(file);
                        RegisterFontFile(file);
                    }

                    return new FontConfigEntry
                    {
                        FontFile = file,
                        FontName = fontName
                    };
                }
            }
        }
        return null;
    }

    internal static bool TryGetFontConfig(Last.Management.FontManager.FontType type, string languageStr, out FontConfigEntry entry)
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

    private void LoadFontConfig()
    {
        FontConfigMapping.Clear();
        LoadedFonts.Clear();
        RegisteredFontFiles.Clear();

        var fontsDir = Path.Combine(ModulesRootPath, "Shared", "Fonts");
        var configPath = Path.Combine(fontsDir, "fontconfig.json");

        if (!Directory.Exists(fontsDir))
        {
            try
            {
                Directory.CreateDirectory(fontsDir);
                PluginLog.LogInfo($"[FontSwap] Created directory: {fontsDir}");
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"[FontSwap] Failed to create directory '{fontsDir}': {ex}");
            }
        }

        var helpPath = Path.Combine(fontsDir, "font-help.txt");
        if (!File.Exists(helpPath))
        {
            try
            {
                var helpText =
@"KupoUI.PR Font Swap Help Guide
=============================

This directory manages custom font swapping for KupoUI.PR.

Files:
- fontconfig.json: Holds active font configurations. Copy target blocks or specific keys from fontconfig-sample.json here to customize them.
- fontconfig-sample.json: Holds game's default baseline values for all supported languages. Overwritten on game launch.
- font-help.txt: This help file.

How to Customize:
1. Open fontconfig-sample.json to reference the game's default font names.
2. Identify the language block (e.g. ""En"", ""Ja"", ""Th"", etc.) and the specific FontType (Font01..Font10) you wish to change. Note that font enums differ per language.
3. In fontconfig.json, create the corresponding language block (ensure it matches the language you are playing in-game) and intentionally define the specific FontType key you want to change.
4. Edit the configuration block:
   - Set ""FontName"" to the desired system font family name (e.g. ""Segoe UI"") or a custom font name.
   - (Optional *does not work atm) Set ""FontFile"" if you wish to use a custom font file placed in the ""Fonts/"" directory.
   - Adjust ""LineSpace"" (decimal factor, e.g. 0.85) or ""FontSize"" (integer) if needed.
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
    ""Font05"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.85  },
    ""Font07"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.85  },
    ""Font08"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.85  },
    ""Font09"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.85  },
    ""Font10"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.85  }
  }
}

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
";
                File.WriteAllText(helpPath, helpText);
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"[FontSwap] Failed to generate font-help.txt: {ex}");
            }
        }

        var samplePath = Path.Combine(fontsDir, "fontconfig-sample.json");
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

        try
        {
            File.WriteAllText(samplePath, templateJson);
            PluginLog.LogInfo($"[FontSwap] Generated/Updated baseline template at {samplePath}");
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"[FontSwap] Failed to generate baseline template fontconfig-sample.json: {ex}");
        }

        if (!File.Exists(configPath))
        {
            try
            {
                var minimalConfigJson =
@"{" + "\n" +
@"  ""NOTE"": ""To customize fonts, define desired language blocks or font keys here. See fontconfig-sample.json for all baseline default values.""," + "\n" +
@"  ""En"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.88 }," + "\n" +
@"    ""Font05"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.88 }," + "\n" +
@"    ""Font07"": { ""FontName"": ""Segoe UI"", ""LineSpace"": 0.88 }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.88 }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.88 }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Courier New"", ""LineSpace"": 0.88 }" + "\n" +
@"  }" + "\n" +
@"}";
                File.WriteAllText(configPath, minimalConfigJson);
                PluginLog.LogInfo($"[FontSwap] Generated default minimal fontconfig.json at {configPath}");
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"[FontSwap] Failed to generate template fontconfig.json: {ex}");
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
                    PluginLog.LogInfo($"[FontSwap] Root language target detected: {fileLanguage}");
                }
            }

            // Helper to register parsed configs
            void AddConfig(Last.Management.FontManager.FontType fontType, Last.Data.Parameters.Language? lang, FontConfigEntry entry, string sourceContext)
            {
                FontConfigMapping[(fontType, lang)] = entry;
                var langStr = lang.HasValue ? lang.Value.ToString() : "Global";
                PluginLog.LogInfo($"[FontSwap] Loaded config ({langStr}) via {sourceContext}: {fontType} -> file='{entry.FontFile}' name='{entry.FontName}' (LineSpace={entry.LineSpace}, FontSize={entry.FontSize})");
            }

            // Cache enum arrays once — Enum.GetValues allocates a new array on every call
            var allLanguages = (Last.Data.Parameters.Language[])Enum.GetValues(typeof(Last.Data.Parameters.Language));
            var allFontTypes = (Last.Management.FontManager.FontType[])Enum.GetValues(typeof(Last.Management.FontManager.FontType));

            // 2. Parse language-specific nested blocks (e.g. "Pt": { ... })
            foreach (var lang in allLanguages)
            {
                var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                if (string.IsNullOrEmpty(langName)) continue;

                var langBlock = ReadSubObject(json, langName);
                if (langBlock == null) continue;

                // Parse specific FontTypes within the language block
                foreach (var fontType in allFontTypes)
                {
                    var fontTypeName = Enum.GetName(typeof(Last.Management.FontManager.FontType), fontType);
                    if (string.IsNullOrEmpty(fontTypeName)) continue;

                    var entry = ParseFontConfigEntry(langBlock, fontTypeName);
                    if (entry != null)
                    {
                        AddConfig(fontType, lang, entry, $"nested block '{langName}'");
                    }
                }
            }

            // 3. Parse root-level configs — collect language block ranges from the original json,
            //    then strip them all in one pass using StringBuilder (avoids O(n²) string rebuilds
            //    and index drift that a mutating-Remove loop would cause).
            var removalRanges = new List<(int index, int length)>();
            foreach (var lang in allLanguages)
            {
                var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                if (string.IsNullOrEmpty(langName)) continue;

                var keyPattern = $"\"{Regex.Escape(langName)}\"\\s*:\\s*\\{{";
                var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                var block = ExtractBalancedBraces(json, match.Index + match.Length - 1);
                if (block == null) continue;

                removalRanges.Add((match.Index, match.Index + match.Length - 1 + block.Length - match.Index));
            }

            // Remove in reverse index order so earlier indices remain valid during removal
            removalRanges.Sort((a, b) => b.index.CompareTo(a.index));
            var rootJsonSb = new System.Text.StringBuilder(json);
            foreach (var (idx, len) in removalRanges)
                rootJsonSb.Remove(idx, len);
            var rootJson = rootJsonSb.ToString();

            // Root FontType keys
            foreach (var fontType in allFontTypes)
            {
                var fontTypeName = Enum.GetName(typeof(Last.Management.FontManager.FontType), fontType);
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
            PluginLog.LogError($"[FontSwap] Failed to load fontconfig.json: {ex}");
        }
    }

    /// <summary>
    /// Returns the registered display name for <paramref name="speakerId"/> if one is defined in the "speakers" block.
    /// Applied unconditionally — takes priority over whatever the game provides for that speaker.
    /// </summary>
    internal static bool TryGetSpeakerNameOverride(string speakerId, out string displayName)
    {
        displayName = null;
        if (string.IsNullOrEmpty(speakerId) || speakerId.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return SpeakerNamesOverride.TryGetValue(speakerId, out displayName);
    }

    /// <summary>
    /// Returns per-message speaker overrides for <paramref name="messageId"/> if one is defined in the "messageOverrides" block.
    /// Either or both of <paramref name="speakerId"/>/<paramref name="speakerName"/> may be non-null.
    /// </summary>
    internal static bool TryGetMessageOverride(string messageId, out string speakerId, out string speakerName)
    {
        speakerId = null;
        speakerName = null;
        if (string.IsNullOrEmpty(messageId) || messageId.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (MessageSpeakerOverrides.TryGetValue(messageId, out var entry))
        {
            speakerId = entry.SpeakerId;
            speakerName = entry.SpeakerName;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Scans all sub-folders under {GameRoot}/Modules/ recursively for files named
    /// "speaker-names.json" and merges them all into <see cref="SpeakerNamesOverride"/>
    /// and <see cref="MessageSpeakerOverrides"/>.
    ///
    /// Files are processed in alphabetical path order. Later files override earlier ones
    /// for duplicate keys (last-writer wins), so more-specific mod folders take priority.
    ///
    /// Supported JSON format per file:
    /// {
    ///   "speakers": { "SPEAKER_77": "Crewman" },
    ///   "messageOverrides": { "E0001_00_001_a_01": { "speakerName": "Crewman" } }
    /// }
    /// Backward-compatible with flat format: { "SPEAKER_77": "Crewman" }
    /// </summary>
    private void LoadSpeakerNames()
    {
        SpeakerNamesOverride.Clear();
        MessageSpeakerOverrides.Clear();

        // ── WRITE SAMPLE FILE ───────────────────────────────────────────────────
        var defaultDir = Path.Combine(ModulesRootPath, "Shared", "SpeakerPortraits");
        var samplePath = Path.Combine(defaultDir, "speaker-names-sample.json");
        try
        {
            if (!Directory.Exists(defaultDir))
            {
                Directory.CreateDirectory(defaultDir);
            }

            var sampleJson =
@"{
  ""_comment"": ""speaker-names.json — place this file in any Modules/ sub-folder to activate it."",

  ""speakers"": {
    ""_comment"": ""Register speaker IDs here. The name is always used when that speaker is active."",
    ""SPEAKER_1"":  ""Warrior of Light"",
    ""SPEAKER_77"": ""Crewman"",
    ""SPEAKER_80"": ""Old Man""
  },

  ""messageOverrides"": {
    ""_comment"": ""Per-dialogue-key overrides. speakerId and speakerName are both optional."",
    ""E0001_00_001_a_01"": { ""speakerId"": ""SPEAKER_77"", ""speakerName"": ""Crewman"" },
    ""E0001_00_002_a_01"": { ""speakerName"": ""Old Man"" }
  }
}";
            File.WriteAllText(samplePath, sampleJson);
        }
        catch (Exception ex)
        {
            PluginLog.LogWarning($"[SpeakerNames] Could not write sample file: {ex.Message}");
        }

        // ── SCAN ALL Modules/ SUB-FOLDERS ──────────────────────────────────────
        if (!Directory.Exists(ModulesRootPath))
        {
            PluginLog.LogInfo($"[SpeakerNames] Modules root not found at '{ModulesRootPath}'. Speaker name overrides disabled.");
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(ModulesRootPath, "speaker-names.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"[SpeakerNames] Failed to scan Modules folder: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            PluginLog.LogInfo($"[SpeakerNames] No speaker-names.json found under '{ModulesRootPath}'. Speaker name overrides disabled.");
            return;
        }

        // ── LOAD AND MERGE EACH FILE ────────────────────────────────────────────
        foreach (var configPath in files)
        {
            try
            {
                var json = File.ReadAllText(configPath);

                // ── SPEAKERS BLOCK ──────────────────────────────────────────────
                var speakersBlock = ReadSubObject(json, "speakers");
                if (speakersBlock != null)
                {
                    // New structured format — parse the "speakers": { ... } block
                    LoadFlatSpeakerPairs(speakersBlock);
                }
                else
                {
                    // Backward-compat: flat format { "SPEAKER_77": "Crewman" }
                    LoadFlatSpeakerPairs(json);
                }

                // ── MESSAGE OVERRIDES BLOCK ─────────────────────────────────────
                var msgBlock = ReadSubObject(json, "messageOverrides");
                if (msgBlock != null)
                {
                    LoadMessageOverrides(msgBlock);
                }

                PluginLog.LogInfo($"[SpeakerNames] Loaded from '{configPath}'.");
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"[SpeakerNames] Failed to load '{configPath}': {ex.Message}");
            }
        }

        PluginLog.LogInfo(
            $"[SpeakerNames] Merged {files.Length} file(s): " +
            $"{SpeakerNamesOverride.Count} speaker registration(s), " +
            $"{MessageSpeakerOverrides.Count} message override(s) total.");
    }


    /// <summary>
    /// Parses flat "KEY": "Value" string pairs from <paramref name="json"/> into <see cref="SpeakerNamesOverride"/>.
    /// Keys beginning with '_' are treated as comments and skipped.
    /// </summary>
    private static void LoadFlatSpeakerPairs(string json)
    {
        var matches = Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var key = m.Groups[1].Value;
            if (key.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }
            SpeakerNamesOverride[key] = m.Groups[2].Value;
        }
    }

    /// <summary>
    /// Parses the "messageOverrides": { "msgKey": { "speakerId": "...", "speakerName": "..." } } block
    /// into <see cref="MessageSpeakerOverrides"/>.
    /// </summary>
    private static void LoadMessageOverrides(string block)
    {
        // Match every "KEY": { entry (non-_-prefixed) and extract its balanced object
        var entryPattern = new Regex("\"([^\"]+)\"\\s*:\\s*\\{");
        foreach (System.Text.RegularExpressions.Match m in entryPattern.Matches(block))
        {
            var msgKey = m.Groups[1].Value;
            if (msgKey.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            // m.Index + m.Length - 1 is the position of the '{'
            var obj = ExtractBalancedBraces(block, m.Index + m.Length - 1);
            if (obj == null)
            {
                continue;
            }

            var idMatch = Regex.Match(obj, "\"speakerId\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var nameMatch = Regex.Match(obj, "\"speakerName\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);

            string speakerId = idMatch.Success && !string.IsNullOrEmpty(idMatch.Groups[1].Value)
                ? idMatch.Groups[1].Value
                : null;
            string speakerName = nameMatch.Success && !string.IsNullOrEmpty(nameMatch.Groups[1].Value)
                ? nameMatch.Groups[1].Value
                : null;

            if (speakerId != null || speakerName != null)
            {
                MessageSpeakerOverrides[msgKey] = (speakerId, speakerName);
                PluginLog.LogInfo($"[SpeakerNames] Message override: '{msgKey}' → speakerId='{speakerId ?? "(keep)"}' speakerName='{speakerName ?? "(keep)"}'.");
            }
        }
    }

    // Config entries referenced by original project
    internal static ConfigEntry<bool> EnableTextureHotReloadConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TextureHotReloadDebounceMsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableDDSTexturesConfig { get; private set; } = null!;
    internal static bool EnableCustomTextures => true;
}
