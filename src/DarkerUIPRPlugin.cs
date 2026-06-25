using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using DarkerUI.PR.Compatibility;
using DarkerUI.PR.Patches;
using DarkerUI.PR.Textures;

namespace DarkerUI.PR;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class DarkerUIPRPlugin : BasePlugin
{
    public const string PluginGuid = "com.faospark.darkerui.pr";
    public const string PluginName = "DarkerUI.PR";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static ConfigEntry<bool> DisableMouseCursorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> ForceVSyncConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SaveHighlightColorConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableCustomTexturesConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TextureRootFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<string> GameTagOverrideConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> LogTextureResolutionConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableTextureHotReloadConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TextureHotReloadDebounceMsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableDDSTexturesConfig { get; private set; } = null!;
    internal static ConfigEntry<string> TextureLoggerConfig { get; private set; } = null!;

    public override void Load()
    {
        PluginLog = Log;

        DisableMouseCursorConfig = Config.Bind(
            "General",
            "DisableMouseCursor",
            true,
            "If true, forces the game cursor to remain hidden.");

        ForceVSyncConfig = Config.Bind(
            "General",
            "ForceVSync",
            true,
            "If true, forces V-Sync on and keeps it from being disabled by the game.");

        SaveHighlightColorConfig = Config.Bind(
            "UI",
            "SaveHighlightColor",
            "DarkNavy",
            "Save slot highlight color override for image_blue. Options: Original, DarkNavy, DarkGreen, DarkViolet, DarkYellow, DarkOrange, Disable.");

        EnableCustomTexturesConfig = Config.Bind(
            "Textures",
            "EnableCustomTextures",
            true,
            "If true, enables custom texture loading and replacement.");

        TextureRootFolderConfig = Config.Bind(
            "Textures",
            "TextureRootFolder",
            "DarkerUI.PR\\Textures",
            "Texture root folder. Relative paths resolve under BepInEx/plugins.");

        GameTagOverrideConfig = Config.Bind(
            "Textures",
            "GameTagOverride",
            string.Empty,
            "Optional override for game folder tag (FF1..FF6). Empty = auto-detect.");

        LogTextureResolutionConfig = Config.Bind(
            "Textures",
            "LogTextureResolution",
            false,
            "If true, logs texture indexing and replacement resolution details.");

        EnableTextureHotReloadConfig = Config.Bind(
            "Textures",
            "EnableTextureHotReload",
            true,
            "If true, watches texture folders and reloads index when files change.");

        TextureHotReloadDebounceMsConfig = Config.Bind(
            "Textures",
            "TextureHotReloadDebounceMs",
            350,
            "Debounce window in milliseconds before rebuilding texture index after file changes.");

        EnableDDSTexturesConfig = Config.Bind(
            "Textures",
            "EnableDDSTextures",
            true,
            "If true, enables loading DDS textures (DXT1/DXT5 and uncompressed RGBA32)." );

        TextureLoggerConfig = Config.Bind(
            "Textures",
            "TextureLogger",
            "Discoveries,Resolutions",
            "Combined texture logger setting. Use comma-separated values: Discoveries, Resolutions, Misses. Use All to log all categories or None to disable logger.");

        var (loggerEnabled, logDiscoveries, logResolutions, logMisses) = ResolveTextureLoggerConfig(TextureLoggerConfig.Value);

        TextureLogger.Initialize(
            loggerEnabled,
            logDiscoveries,
            logResolutions,
            logMisses);

        TextureResolver.Initialize(
            TextureRootFolderConfig.Value,
            GameTagOverrideConfig.Value,
            LogTextureResolutionConfig.Value);

        ExternalModDetector.LogLoadedOptionalMods(Log);

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        ForceVSyncPatch.ApplyNow();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        Log.LogInfo($"DisableMouseCursor = {DisableMouseCursorConfig.Value}");
        Log.LogInfo($"ForceVSync = {ForceVSyncConfig.Value}");
        Log.LogInfo($"SaveHighlightColor = {SaveHighlightColorConfig.Value}");
        Log.LogInfo($"EnableCustomTextures = {EnableCustomTexturesConfig.Value}");
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
