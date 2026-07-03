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
    internal static Dictionary<Last.Management.FontManager.FontType, FontConfigEntry> FontConfigMapping { get; } = new();
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

        if (!File.Exists(configPath))
        {
            try
            {
                var templateJson = 
@"{" + "\n" +
@"  ""// Instructions"": ""Map a FontType enum name (Font01..Font10, Default) to a font file in 00-Mods/Fonts/ (e.g. 'myfont.ttf'). Specify 'FontName' with the font family name (e.g. 'Harrington').""," + "\n" +
@"  ""Font01"": {" + "\n" +
@"    ""FontFile"": ""HARNGTON.TTF""," + "\n" +
@"    ""FontName"": ""Harrington""," + "\n" +
@"    ""LineSpace"": 1.0" + "\n" +
@"  }," + "\n" +
@"  ""Font02"": {" + "\n" +
@"    ""FontFile"": ""example.ttf""," + "\n" +
@"    ""FontName"": ""ExampleFont""," + "\n" +
@"    ""LineSpace"": 1.2," + "\n" +
@"    ""FontSize"": 32" + "\n" +
@"  }," + "\n" +
@"  ""Default"": {" + "\n" +
@"    ""FontFile"": ""HARNGTON.TTF""," + "\n" +
@"    ""FontName"": ""Harrington""" + "\n" +
@"  }" + "\n" +
@"}";
                File.WriteAllText(configPath, templateJson);
                PluginLog.LogInfo($"[FontSwap] Generated default template fontconfig.json at {configPath}");
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
            foreach (Last.Management.FontManager.FontType fontType in Enum.GetValues(typeof(Last.Management.FontManager.FontType)))
            {
                var name = Enum.GetName(typeof(Last.Management.FontManager.FontType), fontType);
                if (string.IsNullOrEmpty(name)) continue;

                // Match object: "Font01" : { ... }
                var objMatch = Regex.Match(json, $"\"{name}\"\\s*:\\s*\\{{([^}}]+)\\}}", RegexOptions.IgnoreCase);
                if (objMatch.Success)
                {
                    var objStr = objMatch.Groups[1].Value;
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
                        FontConfigMapping[fontType] = new FontConfigEntry 
                        { 
                            FontFile = file, 
                            FontName = fontName, 
                            LineSpace = space, 
                            FontSize = size 
                        };
                        PluginLog.LogInfo($"[FontSwap] Custom mapping loaded: {fontType} -> file='{file}' name='{fontName}' (LineSpace={space}, FontSize={size})");
                    }
                }
                else
                {
                    // Match string: "Font01" : "MyFont.ttf"
                    var strMatch = Regex.Match(json, $"\"{name}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (strMatch.Success)
                    {
                        var file = strMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(file))
                        {
                            var fontName = Path.GetFileNameWithoutExtension(file);
                            RegisterFontFile(file);
                            FontConfigMapping[fontType] = new FontConfigEntry 
                            { 
                                FontFile = file, 
                                FontName = fontName 
                            };
                            PluginLog.LogInfo($"[FontSwap] Custom mapping loaded: {fontType} -> file='{file}' name='{fontName}'");
                        }
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
