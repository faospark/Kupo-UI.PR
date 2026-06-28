using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using KupoUI.PR.Compatibility;
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

    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static ConfigEntry<bool> DisableMouseCursorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ForceVSyncConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SaveHighlightColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableCustomTexturesConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableTextureHotReloadConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TextureHotReloadDebounceMsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableDDSTexturesConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UiFramesFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UIThemesFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> UIBgColorFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> CursorsFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> ButtonPromptsFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TextureLoggerConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ScaledDownMenuConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> TitleScreenTextWhiteConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TitleScreenTextFontSizeConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TitleScreenBgColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> TitleScreenTextDisableShadowConfig { get; private set; } = null!;
    internal static bool IsTextureLoggerEnabled { get; private set; }
    public override void Load()
    {
        PluginLog = Log;

        SaveHighlightColorConfig = Config.Bind(
            "UI",
            "SaveHighlightColor",
            "DarkNavy",
            "Save slot highlight color override for image_blue. Options: Original, DarkNavy, DarkGreen, DarkViolet, DarkYellow, DarkOrange, Disable.");

        ScaledDownMenuConfig = Config.Bind(
            "UI",
            "ScaledDownMenu",
            true,
            "If true, scales RootObject/Canvas/aspect_parent/menu_parent/menu_base(Clone) to (0.9, 0.9, 1) when it becomes active.");

        TitleScreenTextWhiteConfig = Config.Bind(
            "UI-Title-Screen",
            "TitleScreenTextWhite",
            false,
            "If true, forces the title screen menu text color to white.");

        TitleScreenTextFontSizeConfig = Config.Bind(
            "UI-Title-Screen",
            "TitleScreenTextFontSize",
            40,
            "Font size for the title screen menu text.");

        TitleScreenBgColorConfig = Config.Bind(
            "UI-Title-Screen",
            "TitleScreenBgColor",
            "original",
            "Color for the title screen background. Options: original, white, black, navy, crimson, violet.");

        TitleScreenTextDisableShadowConfig = Config.Bind(
            "UI-Title-Screen",
            "TitleScreenTextDisableShadow",
            false,
            "If true, disables the Shadow component on the title screen menu text.");

        EnableCustomTexturesConfig = Config.Bind(
            "Textures",
            "EnableCustomTextures",
            true,
            "If true, enables custom texture loading and replacement.");

        UIThemesFolderConfig = Config.Bind(
            "Modules",
            "UIThemesFolder",
            "Default",
            "Specify a theme pack under 01-UI-Themes. Overrides 00-Mods but is overridden by specific pack folders. Default = means it will do nothing.");

        UiFramesFolderConfig = Config.Bind(
            "Modules",
            "UIFramesFolder",
            "Default",
            "Specify Folder to Override UI Frames 02-UI-Frames. Default = means it will do nothing.");

        UIBgColorFolderConfig = Config.Bind(
            "Modules",
            "UIBgColorFolder",
            "Default",
            "Specify Folder to Override UI Background Colors 03-UI-BgColor. Default = means it will do nothing.");

        CursorsFolderConfig = Config.Bind(
            "Modules",
            "CursorsFolder",
            "Default",
            "Specify Folder to Override Cursors 04-UI-Cursors. Default = means it will do nothing.");

        ButtonPromptsFolderConfig = Config.Bind(
            "Modules",
            "ButtonPromptsFolder",
            "Default",
            "Specify Folder to Override Button Prompts 05-Button-Prompts. Default = means it will do nothing.");

        DisableMouseCursorConfig = Config.Bind(
            "Utility",
            "DisableMouseCursor",
            true,
            "If true, forces the game cursor to remain hidden.");

        ForceVSyncConfig = Config.Bind(
            "Utility",
            "ForceVSync",
            true,
            "If true, forces V-Sync on and keeps it from being disabled by the game.");

        TextureLoggerConfig = Config.Bind(
            "Utility",
            "TextureLogger",
            "None",
            "Combined texture logger setting. Use comma-separated values: Discoveries, Resolutions, Misses. Use All to log all categories or None to disable logger.");
        
        EnableTextureHotReloadConfig = Config.Bind(
            "Utility",
            "EnableTextureHotReload",
            true,
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

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        Log.LogInfo($"DisableMouseCursor = {DisableMouseCursorConfig.Value}");
        Log.LogInfo($"ForceVSync = {ForceVSyncConfig.Value}");
        Log.LogInfo($"SaveHighlightColor = {SaveHighlightColorConfig.Value}");
        Log.LogInfo($"EnableCustomTextures = {EnableCustomTexturesConfig.Value}");
        Log.LogInfo($"ScaledDownMenu = {ScaledDownMenuConfig.Value}");
        Log.LogInfo($"TitleScreenTextWhite = {TitleScreenTextWhiteConfig.Value}");
        Log.LogInfo($"TitleScreenTextFontSize = {TitleScreenTextFontSizeConfig.Value}");
        Log.LogInfo($"TitleScreenBgColor = {TitleScreenBgColorConfig.Value}");
        Log.LogInfo($"TitleScreenTextDisableShadow = {TitleScreenTextDisableShadowConfig.Value}");
    }

    private static (bool enabled, bool logDiscoveries, bool logResolutions, bool logMisses) ResolveTextureLoggerConfig(string configValue)
    {
        if (string.IsNullOrWhiteSpace(configValue))
        {
            return (false, false, false, false);
        }

        var enabled = true;
        var logDiscoveries = false;
        var logResolutions = false;
        var logMisses = false;

        var tokens = configValue.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawToken in tokens)
        {
            var token = rawToken.Trim();
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return (true, true, true, true);
            }

            if (token.Equals("none", StringComparison.OrdinalIgnoreCase)
                || token.Equals("off", StringComparison.OrdinalIgnoreCase)
                || token.Equals("disabled", StringComparison.OrdinalIgnoreCase)
                || token.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return (false, false, false, false);
            }

            if (token.Equals("discoveries", StringComparison.OrdinalIgnoreCase) || token.Equals("discovery", StringComparison.OrdinalIgnoreCase))
            {
                logDiscoveries = true;
                continue;
            }

            if (token.Equals("resolutions", StringComparison.OrdinalIgnoreCase) || token.Equals("resolution", StringComparison.OrdinalIgnoreCase))
            {
                logResolutions = true;
                continue;
            }

            if (token.Equals("misses", StringComparison.OrdinalIgnoreCase) || token.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                logMisses = true;
                continue;
            }

            if (token.Equals("enabled", StringComparison.OrdinalIgnoreCase) || token.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
            }
        }

        if (!logDiscoveries && !logResolutions && !logMisses)
        {
            return (false, false, false, false);
        }

        return (enabled, logDiscoveries, logResolutions, logMisses);
    }
}
