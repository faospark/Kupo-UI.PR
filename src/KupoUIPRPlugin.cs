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
    internal static ConfigEntry<string> TextureLoggerConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ScaledDownMenuConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TitleScreenBgColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> MessageSpeakerPrefixConfig { get; private set; } = null!;
    internal static ConfigEntry<string> DialogueFontSizeConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> MessageSpeakerPrefixLoggingConfig { get; private set; } = null!;
    internal static bool IsTextureLoggerEnabled { get; private set; }

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
    private static readonly List<string> RegisteredFontFiles = new();

    public override void Load()
    {
        PluginLog = Log;





        FontSwapEnabledConfig = Config.Bind(
            "FontSwap",
            "Enabled",
            false,
            "If true, swaps default game fonts with custom font files defined in Modules/00-Mods/Fonts/fontconfig.json."
        );

        DiagnosticsLogFontMappingConfig = Config.Bind(
            "Diagnostics",
            "LogFontMapping",
            true,
            "If true, logs information about FontManager.CreateFontParameter and set_FontInstance requests to identify FontType mappings."
        );

        SaveHighlightColorConfig = Config.Bind(
            "UI",
            "SaveHighlightColor",
            "Disable",
            "Save slot highlight color override for image_blue. Options: Original, DarkNavy, DarkGreen, DarkViolet, DarkYellow, DarkOrange, Disable.");

        ScaledDownMenuConfig = Config.Bind(
            "UI",
            "ScaledDownMenu",
            true,
            "If true, scales RootObject/Canvas/aspect_parent/menu_parent/menu_base(Clone) to (0.9, 0.9, 1) when it becomes active.");

        TitleScreenBgColorConfig = Config.Bind(
            "UI-Title-Screen",
            "TitleScreenBgColor",
            "original",
            "Color for the title screen background. Options: original, white, black, navy, crimson, violet.");

        DialogueFontSizeConfig = Config.Bind(
            "UI-Text-Size",
            "DialogueFontSize",
            "36",
            "Font size to use for Dialogue Text UI. Defualt is 36. This value can scale up to 48.");

        MessageSpeakerPrefixConfig = Config.Bind(
            "UI-Message-Tweaks",
            "MessageSpeakerPrefix",
            false,
            "If true, adds a prefix to the message window speaker text to display the speaker name.");

        MessageSpeakerPrefixLoggingConfig = Config.Bind(
            "UI-Message-Tweaks",
            "MessageSpeakerPrefixLogging",
            true,
            "If true, logs speaker name replacements.");

        DisableMouseCursorConfig = Config.Bind(
            "UI-Cursor",
            "DisableMouseCursor",
            true,
            "If true, disables the default mouse cursor inside game frame.");

        ForceVSyncConfig = Config.Bind(
            "UI-Graphics",
            "ForceVSync",
            true,
            "If true, forces VSync on startup.");

        UiFramesFolderConfig = Config.Bind(
            "Textures-Resolving",
            "UiFramesFolder",
            "UiFrames",
            "Target folder for ResolveTexture: UI frame overrides.");

        UIThemesFolderConfig = Config.Bind(
            "Textures-Resolving",
            "UIThemesFolder",
            "UIThemes",
            "Target folder for ResolveTexture: UI theme overrides.");

        UIBgColorFolderConfig = Config.Bind(
            "Textures-Resolving",
            "UIBgColorFolder",
            "UIBgColor",
            "Target folder for ResolveTexture: UI background color overrides.");

        CursorsFolderConfig = Config.Bind(
            "Textures-Resolving",
            "CursorsFolder",
            "Cursors",
            "Target folder for ResolveTexture: Cursor overrides.");

        ButtonPromptsFolderConfig = Config.Bind(
            "Textures-Resolving",
            "ButtonPromptsFolder",
            "ButtonPrompts",
            "Target folder for ResolveTexture: Button prompt overrides.");

        TextureLoggerConfig = Config.Bind(
            "Diagnostics",
            "TextureLogger",
            "Off",
            "Texture Resolution Logger mode: Off, Discoveries, Resolutions, Misses, All");

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
            "Experimenta - If true, enables loading DDS textures (DXT1/DXT5 and uncompressed RGBA32)." );

        var (loggerEnabled, logDiscoveries, logResolutions, logMisses) = ResolveTextureLoggerConfig(TextureLoggerConfig.Value);

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

        if (MessageSpeakerPrefixConfig.Value)
        {
            ObjectConfigLoader.AddEntry(new ObjectConfig.ObjectConfigEntry
            {
                TargetObjectName = "speker_root",
                TargetPath = "RootObject/Canvas/UIParent/message_parent(Clone)/parent_root/upper_parent/message_window(Clone)/speker_root",
                Position = new ObjectConfig.Vec3 { X = -730f, Y = -5580f, Z = 0f },
                Scale = new ObjectConfig.Vec3 { X = 0.9f, Y = 0.9f, Z = 1f },
                SourceFile = "KupoUIPRPlugin.cs"
            });
        }

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        Log.LogInfo($"DisableMouseCursor = {DisableMouseCursorConfig.Value}");
        Log.LogInfo($"ForceVSync = {ForceVSyncConfig.Value}");
        Log.LogInfo($"SaveHighlightColor = {SaveHighlightColorConfig.Value}");
        Log.LogInfo($"EnableCustomTextures = {EnableCustomTextures}");
        Log.LogInfo($"ScaledDownMenu = {ScaledDownMenuConfig.Value}");
        Log.LogInfo($"TitleScreenBgColor = {TitleScreenBgColorConfig.Value}");
        Log.LogInfo($"MessageSpeakerPrefix = {MessageSpeakerPrefixConfig.Value}");
        Log.LogInfo($"DialogueFontSize = {DialogueFontSizeConfig.Value}");
        Log.LogInfo($"MessageSpeakerPrefixLogging = {MessageSpeakerPrefixLoggingConfig.Value}");
        Log.LogInfo($"FontSwapEnabled = {FontSwapEnabledConfig.Value}");
        Log.LogInfo($"DiagnosticsLogFontMapping = {DiagnosticsLogFontMappingConfig.Value}");
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
            catch {}
        }
        RegisteredFontFiles.Clear();
    }

    private static (bool enabled, bool logDiscoveries, bool logResolutions, bool logMisses) ResolveTextureLoggerConfig(string configValue)
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
        var fontFilePath = Path.Combine(ModulesRootPath, "00-Mods", "Fonts", fontFile);
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

        var fontsDir = Path.Combine(ModulesRootPath, "00-Mods", "Fonts");
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
   - (Optional) Set ""FontFile"" if you wish to use a custom font file placed in the ""Fonts/"" directory.
   - Adjust ""LineSpace"" (decimal factor, e.g. 0.85) or ""FontSize"" (integer) if needed.
5. Restart the game to apply changes.

Example fontconfig.json (Limited Scope Override):
{
  ""En"": {
    ""Font01"": { ""FontName"": ""Segoe UI"" },
    ""Font07"": { ""FontName"": ""Segoe UI"" },
    ""Font08"": { ""FontName"": ""Courier New"" },
    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" },
    ""Font10"": { ""FontName"": ""Courier New"" }
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

        var samplePath = Path.Combine(fontsDir, "fontconfig-sample.json");
        var templateJson = 
@"{" + "\n" +
@"  ""En"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""sqex-MonoSix"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""Ja"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""Fr"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""sqex-MonoSix"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""De"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""sqex-MonoSix"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""It"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""sqex-MonoSix"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""Ru"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""ITCAvantGardeW1G-Medium"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Arial"" }" + "\n" +
@"  }," + "\n" +
@"  ""Pt"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""SE-ALPSCB__"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""sqex-MonoSix"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""sqex-MonoSix"" }" + "\n" +
@"  }," + "\n" +
@"  ""Th"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""arnewhebesans-th_rg"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""arnewhebesans-th_rg"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""arnewhebesans-th_rg"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""FOT-NewRodinPro-DB"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Arial"" }" + "\n" +
@"  }," + "\n" +
@"  ""Ko"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""FOTK-YoonGothic750"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Arial"" }" + "\n" +
@"  }," + "\n" +
@"  ""Zht"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""arudjingxiheiu30_db"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Arial"" }" + "\n" +
@"  }," + "\n" +
@"  ""Zhc"": {" + "\n" +
@"    ""Font01"": { ""FontName"": ""arudjingxiheig30_db"" }," + "\n" +
@"    ""Font02"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font03"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font04"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font05"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font06"": { ""FontName"": ""FOT-NewCezannePro-B"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""SE-ALPSTN__"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""Arial"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Arial"" }" + "\n" +
@"  }" + "\n" +
@"}";

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
@"    ""Font01"": { ""FontName"": ""Segoe UI"" }," + "\n" +
@"    ""Font07"": { ""FontName"": ""Segoe UI"" }," + "\n" +
@"    ""Font08"": { ""FontName"": ""Code"" }," + "\n" +
@"    ""Font09"": { ""FontName"": ""PIXELREMASTERFONT"" }," + "\n" +
@"    ""Font10"": { ""FontName"": ""Code"" }" + "\n" +
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

            // 2. Parse language-specific nested blocks (e.g. "Pt": { ... })
            foreach (Last.Data.Parameters.Language lang in Enum.GetValues(typeof(Last.Data.Parameters.Language)))
            {
                var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                if (string.IsNullOrEmpty(langName)) continue;

                var langBlock = ReadSubObject(json, langName);
                if (langBlock != null)
                {


                    // Parse specific FontTypes within the language block
                    foreach (Last.Management.FontManager.FontType fontType in Enum.GetValues(typeof(Last.Management.FontManager.FontType)))
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
            }

            // 3. Parse root-level configs by removing language blocks first
            var rootJson = json;
            foreach (Last.Data.Parameters.Language lang in Enum.GetValues(typeof(Last.Data.Parameters.Language)))
            {
                var langName = Enum.GetName(typeof(Last.Data.Parameters.Language), lang);
                if (string.IsNullOrEmpty(langName)) continue;

                var langBlock = ReadSubObject(rootJson, langName);
                if (langBlock != null)
                {
                    var keyPattern = $"\"{Regex.Escape(langName)}\"\\s*:\\s*\\{{";
                    var match = Regex.Match(rootJson, keyPattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var blockIndex = rootJson.IndexOf(langBlock, match.Index);
                        if (blockIndex >= 0)
                        {
                            var blockLength = (blockIndex + langBlock.Length) - match.Index;
                            rootJson = rootJson.Remove(match.Index, blockLength);
                        }
                    }
                }
            }

            // Root FontType keys
            foreach (Last.Management.FontManager.FontType fontType in Enum.GetValues(typeof(Last.Management.FontManager.FontType)))
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
                foreach (Last.Data.Parameters.Language lang in Enum.GetValues(typeof(Last.Data.Parameters.Language)))
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

    // Config entries referenced by original project
    internal static ConfigEntry<bool> EnableTextureHotReloadConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TextureHotReloadDebounceMsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableDDSTexturesConfig { get; private set; } = null!;
    internal static bool EnableCustomTextures => true;
}
