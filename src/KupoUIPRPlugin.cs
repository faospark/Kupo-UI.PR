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
using KupoUI.PR.IconsConfig;

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
    internal static ConfigEntry<bool> SpeakerNameNewLineConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DialogueTextWrapConfig { get; private set; } = null!;
    internal static ConfigEntry<int> DialogueLineLengthLimitConfig { get; private set; } = null!;
    internal static ConfigEntry<string> DialogueFontSizeConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticMessageSpeakerPrefixLoggingConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticLogAllTextsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticIconLoggingConfig { get; private set; } = null!;
    internal static bool IsTextureLoggerEnabled { get; private set; }

    internal static ConfigEntry<bool> EnableSpeakerPortraitsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticPortraitLoggingConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> FlipSpeakerPortraitsConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SpeakerPortraitsPaddingConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SpeakerPortraitsTextOffsetConfig { get; private set; } = null!;

    /// <summary>
    /// Speaker ID → display name registrations loaded from the "speakers" block of SpeakerNames.json / speaker-names.json.
    /// Always applied when the speaker ID matches — not limited to blank-name fallback.
    /// </summary>
    internal static Dictionary<string, string> SpeakerNamesOverride { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dialogue message key → (speakerId override, speakerName override) loaded from the "messageOverrides" block.
    /// Takes highest priority — overrides both the game's speaker ID and name for a specific dialogue line.
    /// </summary>
    internal static Dictionary<string, (string SpeakerId, string SpeakerName)> MessageSpeakerOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Menu portrait asset address -> Speaker ID, name, or image filename override mapping.
    /// Loaded from MenuPortraitMap.json files under BepInEx Modules folders.
    /// </summary>
    internal static Dictionary<string, string> MenuPortraitMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static ConfigEntry<bool> FontSwapEnabledConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticsLogFontMappingConfig { get; private set; } = null!;





    public override void Load()
    {
        PluginLog = Log;

        FontSwapEnabledConfig = Config.Bind(
            "FontSwap",
            "Enabled",
            false,
            "If true, swaps default game fonts with custom fonts defined in Modules/Shared/fontconfig.json.");

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
            "Auto",
            "Font size to use for Dialogue Text UI. Default is Auto. This value can scale up to 48-ish; you can even set to Auto to use the font's declared size in game");

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

        SpeakerNameNewLineConfig = Config.Bind(
            "UI-Dialog",
            "SpeakerNameNewLine",
            false,
            "If true, inserts a line break (new line) after the speaker prefix in dialogue boxes to prevent text overflow.");

        DialogueTextWrapConfig = Config.Bind(
            "UI-Dialog",
            "DialogueTextWrap",
            true,
            "If true, forces built-in text wrapping on dialogue text boxes to prevent horizontal overflow.");

        DialogueLineLengthLimitConfig = Config.Bind(
            "UI-Dialog",
            "DialogueLineLengthLimit",
            0,
            "If greater than 0, forces dialogue text to wrap at this maximum character count per line. Useful when prepending speaker names to prevent text overflow.");

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

        SpeakerPortraitsPaddingConfig = Config.Bind(
            "UI-Dialog",
            "SpeakerPortraitsPadding",
            "0,0,0,0",
            "Padding for speaker portraits. Format: 'left,top,right,bottom' in pixels (e.g. '10,15,0,20').");

        SpeakerPortraitsTextOffsetConfig = Config.Bind(
            "UI-Dialog",
            "SpeakerPortraitsTextOffset",
            "0",
            "Offset (in pixels) for the dialogue text box (lastText) when speaker portraits are active. Supports 'X' or 'X,Y' format (e.g. '-75' or '-75,10'). Positive X moves right, positive Y moves up.");

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

        DiagnosticLogAllTextsConfig = Config.Bind(
            "Z - Diagnostics",
            "LogAllTexts",
            false,
            "If true, logs all texts assigned to UnityEngine.UI.Text components to the console.");

        DiagnosticIconLoggingConfig = Config.Bind(
            "Z - Diagnostics",
            "IconLogging",
            false,
            "If true, logs custom icon tag matches and sprite swaps to the console.");

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

        FontResolver.Initialize(ModulesRootPath);

        TextureResolver.Initialize(
            TextureRootFolder,
            UiFramesFolderConfig.Value,
            UIThemesFolderConfig.Value,
            UIBgColorFolderConfig.Value,
            CursorsFolderConfig.Value,
            ButtonPromptsFolderConfig.Value);

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        ForceVSyncPatch.ApplyNow();
        ObjectConfigPatch.Initialize(ModulesRootPath);
        TextConfigPatch.Initialize(ModulesRootPath);
        IconsConfigLoader.Initialize(ModulesRootPath);
        TextConfigPatch.PatchItemListContentData(harmony);
        TextConfigPatch.PatchShopListContentData(harmony);

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
        Log.LogInfo($"SpeakerNameNewLine = {SpeakerNameNewLineConfig.Value}");
        Log.LogInfo($"DialogueTextWrap = {DialogueTextWrapConfig.Value}");
        Log.LogInfo($"DialogueLineLengthLimit = {DialogueLineLengthLimitConfig.Value}");
        Log.LogInfo($"DialogueFontSize = {DialogueFontSizeConfig.Value}");
        Log.LogInfo($"MessageSpeakerPrefixLogging = {DiagnosticMessageSpeakerPrefixLoggingConfig.Value}");
        Log.LogInfo($"LogAllTexts = {DiagnosticLogAllTextsConfig.Value}");
        Log.LogInfo($"IconLogging = {DiagnosticIconLoggingConfig.Value}");
        Log.LogInfo($"FontSwapEnabled = {FontSwapEnabledConfig.Value}");
        Log.LogInfo($"DiagnosticsLogFontMapping = {DiagnosticsLogFontMappingConfig.Value}");

        Log.LogInfo($"EnableSpeakerPortraits = {EnableSpeakerPortraitsConfig.Value}");
        Log.LogInfo($"PortraitLogging = {DiagnosticPortraitLoggingConfig.Value}");
        Log.LogInfo($"SpeakerPortraitsPadding = {SpeakerPortraitsPaddingConfig.Value}");
        Log.LogInfo($"SpeakerPortraitsTextOffset = {SpeakerPortraitsTextOffsetConfig.Value}");

        LoadSpeakerNames();
        LoadMenuPortraitMaps();
        WriteTextConfigSample();
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



    internal static string ExtractBalancedBraces(string json, int openBraceIndex)
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

    internal static string ReadSubObject(string json, string key)
    {
        var keyPattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\{{";
        var match = Regex.Match(json, keyPattern, RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        return ExtractBalancedBraces(json, match.Index + match.Length - 1);
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
    /// "SpeakerNames.json" or "speaker-names.json" and merges them all into <see cref="SpeakerNamesOverride"/>
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



        // ── SCAN ALL Modules/ SUB-FOLDERS ──────────────────────────────────────
        if (!Directory.Exists(ModulesRootPath))
        {
            PluginLog.LogInfo($"[SpeakerNames] Modules root not found at '{ModulesRootPath}'. Speaker name overrides disabled.");
            return;
        }

        string[] files;
        try
        {
            var oldFiles = Directory.GetFiles(ModulesRootPath, "speaker-names.json", SearchOption.AllDirectories);
            var newFiles = Directory.GetFiles(ModulesRootPath, "SpeakerNames.json", SearchOption.AllDirectories);

            var fileList = new System.Collections.Generic.List<string>(oldFiles);
            foreach (var f in newFiles)
            {
                bool exists = false;
                foreach (var existing in fileList)
                {
                    if (string.Equals(existing, f, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    fileList.Add(f);
                }
            }
            files = fileList.ToArray();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"[SpeakerNames] Failed to scan Modules folder: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            PluginLog.LogInfo($"[SpeakerNames] No speaker-names.json or SpeakerNames.json found under '{ModulesRootPath}'. Speaker name overrides disabled.");
            return;
        }

        // ── LOAD AND MERGE EACH FILE ────────────────────────────────────────────
        var gameTag = Textures.TextureResolver.CurrentGameTag;
        foreach (var configPath in files)
        {
            var normalizedPath = configPath.Replace('\\', '/');
            var pathSegments = normalizedPath.Split('/');
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

    private void LoadMenuPortraitMaps()
    {
        MenuPortraitMap.Clear();



        // ── SCAN ALL Modules/ SUB-FOLDERS ──────────────────────────────────────
        if (!Directory.Exists(ModulesRootPath))
        {
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(ModulesRootPath, "MenuPortraitMap.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"[MenuPortraitMap] Failed to scan Modules folder: {ex.Message}");
            return;
        }

        // ── LOAD AND MERGE EACH FILE ────────────────────────────────────────────
        var gameTag = Textures.TextureResolver.CurrentGameTag;
        foreach (var configPath in files)
        {
            var normalizedPath = configPath.Replace('\\', '/');
            var pathSegments = normalizedPath.Split('/');
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

            try
            {
                var json = File.ReadAllText(configPath);
                LoadFlatMenuPortraitPairs(json);
                PluginLog.LogInfo($"[MenuPortraitMap] Loaded from '{configPath}'.");
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"[MenuPortraitMap] Failed to load '{configPath}': {ex.Message}");
            }
        }

        PluginLog.LogInfo($"[MenuPortraitMap] Merged {files.Length} file(s): {MenuPortraitMap.Count} mapping(s) total.");
    }

    private static void LoadFlatMenuPortraitPairs(string json)
    {
        var matches = Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var key = m.Groups[1].Value;
            if (key.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }
            MenuPortraitMap[key] = m.Groups[2].Value;
        }
    }

    private void WriteTextConfigSample()
    {
        var defaultDir = Path.Combine(ModulesRootPath, "Shared");
        var samplePath = Path.Combine(defaultDir, "TextConfig-sample.json");
        try
        {
            if (!Directory.Exists(defaultDir))
            {
                Directory.CreateDirectory(defaultDir);
            }

            var sampleJson =
@"{
  ""_comment"": ""TextConfig-sample.json — Scopes language and overrides menu and dialogue texts additively."",
  ""Language"": ""En"",
  ""texts"": {
    ""MSG_SYSTEM_002"": ""Go Back!"",
    ""MSG_SYSTEM_022"": ""Confirm Override"",
    ""Confirm"": ""Yes""
  },
  ""objects"": [
    {
      ""TargetObjectName"": ""value_text"",
      ""TargetPath"": ""LocationParent/location/value_text"",
      ""NewText"": ""Tower of Worship (Modified)""
    }
  ]
}";
            File.WriteAllText(samplePath, sampleJson);
        }
        catch (Exception ex)
        {
            PluginLog.LogWarning($"[TextConfig] Could not write sample file: {ex.Message}");
        }
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
            }
        }
    }

    // Config entries referenced by original project
    internal static ConfigEntry<bool> EnableTextureHotReloadConfig { get; private set; } = null!;
    internal static ConfigEntry<int> TextureHotReloadDebounceMsConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableDDSTexturesConfig { get; private set; } = null!;
    internal static bool EnableCustomTextures => true;
}
