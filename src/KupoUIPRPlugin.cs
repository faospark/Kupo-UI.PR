using System;
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
    internal static bool EnableCustomTextures => true;
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
    internal static ConfigEntry<string> TitleScreenBgColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> MessageSpeakerPrefixConfig { get; private set; } = null!;
    internal static ConfigEntry<string> DialogueFontSizeConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> MessageSpeakerPrefixLoggingConfig { get; private set; } = null!;
    internal static bool IsTextureLoggerEnabled { get; private set; }
    public override void Load()
    {
        PluginLog = Log;

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

        MessageSpeakerPrefixConfig = Config.Bind(
            "UI-Dialogbox",
            "MessageSpeakerPrefix",
            false,
            "If true, prepends the speaker name to every dialogue message (e.g. \"Maria: I have been worried\" instead of \"I have been worried\").");

        DialogueFontSizeConfig = Config.Bind(
            "UI-Dialogbox",
            "DialogueFontSize",
            "36",
            "Font size applied to dialogue message text and speaker label. " +
            "Independent of MessageSpeakerPrefix — works on its own. " +
            "Use 'Auto' to leave the original font sizes untouched. " +
            "Set a numeric value (e.g. 24) to override. Recommended starting value if you see overflow: 24.");

        MessageSpeakerPrefixLoggingConfig = Config.Bind(
            "UI-Dialogbox",
            "MessageSpeakerPrefixLogging",
            true,
            "If true, outputs dialogue match logs to the BepInEx console (helpful for extracting dialogue keys/IDs).");


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
