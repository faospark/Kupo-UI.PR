using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace KupoUI.PR.ObjectConfig;

/// <summary>
/// Scans every <c>ObjectConfig.json</c> file found recursively under
/// <c>&lt;GameRoot&gt;/Modules/</c> and exposes the merged list of
/// <see cref="ObjectConfigEntry"/> objects.
/// Files inside <c>Shared/FF1</c>–<c>FF6</c> sub-folders are filtered to the
/// detected current game tag so only the matching game's rules are applied.
/// </summary>
internal static class ObjectConfigLoader
{
    private const string ConfigFileName = "ObjectConfig.json";

    private static readonly List<ObjectConfigEntry> _entries = new();

    /// <summary>All loaded transform rules, across every discovered mod folder.</summary>
    internal static IReadOnlyList<ObjectConfigEntry> Entries => _entries;

    /// <summary>
    /// Discovers and parses all <c>ObjectConfig.json</c> files found anywhere
    /// under <paramref name="modulesRootPath"/>. Files inside
    /// <c>Shared/FF1</c>–<c>FF6</c> sub-folders are filtered to the active game tag.
    /// Safe to call multiple times; previous entries are cleared on each call.
    /// </summary>
    /// <param name="modulesRootPath">
    /// Absolute path to the <c>Modules</c> folder (i.e. <c>Paths.GameRootPath + "/Modules"</c>).
    /// </param>
    internal static void Load(string modulesRootPath)
    {
        _entries.Clear();
        var filesToLoad = new List<string>();

        if (!Directory.Exists(modulesRootPath))
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[ObjectConfig] Modules root not found, skipping: {modulesRootPath}");
            return;
        }

        var gameTag = Textures.TextureResolver.CurrentGameTag;
        var sharedRoot = Path.Combine(modulesRootPath, "Shared");
        var normalizedSharedRoot = sharedRoot.Replace('\\', '/').TrimEnd('/') + "/";

        // Gather all ObjectConfig.json files under Modules/ recursively.
        var allFiles = Directory.GetFiles(modulesRootPath, ConfigFileName, SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var normalizedFile = file.Replace('\\', '/');

            // Apply game-tag filtering for files inside Shared/FFx/ sub-folders.
            if (normalizedFile.StartsWith(normalizedSharedRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relPath = normalizedFile.Substring(normalizedSharedRoot.Length);
                var firstSegment = relPath.Split('/')[0];

                var isGameTagFolder =
                    firstSegment.Equals("FF1", StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals("FF2", StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals("FF3", StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals("FF4", StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals("FF5", StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals("FF6", StringComparison.OrdinalIgnoreCase);

                if (isGameTagFolder && !firstSegment.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Skip configs for other games.
                }
            }

            filesToLoad.Add(file);
        }

        if (filesToLoad.Count == 0)
        {
            KupoUIPRPlugin.PluginLog.LogInfo("[ObjectConfig] No ObjectConfig.json files found.");
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
            $"[ObjectConfig] Loaded {totalLoaded} rule(s) from {filesToLoad.Count} file(s).");
    }

    /// <summary>
    /// Adds a single rule at runtime.
    /// </summary>
    internal static void AddEntry(ObjectConfigEntry entry)
    {
        if (entry == null) return;
        _entries.Add(entry);
        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[ObjectConfig] Runtime entry added: name='{entry.TargetObjectName}'" +
            (string.IsNullOrEmpty(entry.TargetPath) ? "" : $" path='{entry.TargetPath}'"));
    }

    // -------------------------------------------------------------------------
    // JSON parsing (hand-rolled to match the project's existing pattern and
    // avoid adding a JSON library dependency)
    // -------------------------------------------------------------------------

    private static List<ObjectConfigEntry> ParseFile(string filePath)
    {
        var result = new List<ObjectConfigEntry>();
        try
        {
            var json = File.ReadAllText(filePath);

            // Extract the "objects" array content.
            var arrayContent = ExtractArrayContent(json, "objects");
            if (arrayContent == null)
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] No 'objects' array found in: {filePath}");
                return result;
            }

            // Split array into individual object blocks.
            var objectBlocks = SplitObjectBlocks(arrayContent);
            foreach (var block in objectBlocks)
            {
                var entry = ParseEntry(block, filePath);
                if (entry != null)
                {
                    result.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning(
                $"[ObjectConfig] Failed to parse '{filePath}': {ex.Message}");
        }

        return result;
    }

    private static ObjectConfigEntry ParseEntry(string block, string sourceFile)
    {
        var name = ReadString(block, "TargetObjectName");
        if (string.IsNullOrWhiteSpace(name))
        {
            KupoUIPRPlugin.PluginLog.LogWarning(
                $"[ObjectConfig] Skipping entry without 'TargetObjectName' in: {sourceFile}");
            return null;
        }

        var entry = new ObjectConfigEntry
        {
            TargetObjectName = name.Trim(),
            TargetPath       = ReadString(block, "TargetPath")?.Trim(),
            SceneName        = ReadString(block, "SceneName")?.Trim(),
            SetActive        = ReadBool(block, "SetActive"),
            TextAlignment    = ReadString(block, "TextAlignment")?.Trim(),
            ChildAlignment   = ReadString(block, "ChildAlignment")?.Trim(),
            FontSize         = ReadInt(block, "FontSize"),
            ResizeTextForBestFit = ReadBool(block, "ResizeTextForBestFit"),
            ResizeTextMaxSize    = ReadInt(block, "ResizeTextMaxSize"),
            ResizeTextMinSize    = ReadInt(block, "ResizeTextMinSize"),
            TextColorWhite       = ReadBool(block, "TextColorWhite"),
            DisableShadow        = ReadBool(block, "DisableShadow"),
            SourceFile       = sourceFile,
        };

        // Position
        var posBlock = ReadSubObject(block, "Position");
        if (posBlock != null)
        {
            entry.Position = new Vec3
            {
                X = ReadFloat(posBlock, "x"),
                Y = ReadFloat(posBlock, "y"),
                Z = ReadFloat(posBlock, "z"),
            };
        }

        // Rotation
        var rotBlock = ReadSubObject(block, "Rotation");
        if (rotBlock != null)
        {
            entry.Rotation = new Vec3
            {
                X = ReadFloat(rotBlock, "x"),
                Y = ReadFloat(rotBlock, "y"),
                Z = ReadFloat(rotBlock, "z"),
            };
        }

        // Scale
        var scaleBlock = ReadSubObject(block, "Scale");
        if (scaleBlock != null)
        {
            entry.Scale = new Vec3
            {
                X = ReadFloat(scaleBlock, "x"),
                Y = ReadFloat(scaleBlock, "y"),
                Z = ReadFloat(scaleBlock, "z"),
            };
        }

        // Color
        var colorStr = ReadString(block, "Color");
        if (!string.IsNullOrWhiteSpace(colorStr))
        {
            if (TryParseColorString(colorStr, out var parsedColor))
            {
                entry.Color = parsedColor;
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] Unable to parse Color string '{colorStr}' in: {sourceFile}");
            }
        }
        else
        {
            var colorBlock = ReadSubObject(block, "Color");
            if (colorBlock != null)
            {
                entry.Color = ParseColorObject(colorBlock);
            }
        }

        return entry;
    }

    // ---- Primitive readers --------------------------------------------------

    private static string ReadString(string json, string key)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static float ReadFloat(string json, string key)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(key)}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);
        return match.Success
            && float.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
            ? value
            : 0f;
    }

    private static bool? ReadBool(string json, string key)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(key)}\"\\s*:\\s*(true|false)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return bool.TryParse(match.Groups[1].Value, out var v) ? v : (bool?)null;
    }

    private static int? ReadInt(string json, string key)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(key)}\"\\s*:\\s*(-?\\d+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var v) ? v : (int?)null;
    }

    /// <summary>
    /// Extracts the content of a named sub-object block, e.g. <c>"Position": { ... }</c>
    /// returns the text between the braces.
    /// </summary>
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

    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "red", Color.red },
        { "green", Color.green },
        { "blue", Color.blue },
        { "white", Color.white },
        { "black", Color.black },
        { "yellow", Color.yellow },
        { "cyan", Color.cyan },
        { "magenta", Color.magenta },
        { "gray", Color.gray },
        { "grey", Color.gray },
        { "clear", Color.clear },
        { "navy", new Color(0f, 0f, 0.5f, 1f) },
        { "crimson", new Color(0.86f, 0.08f, 0.24f, 1f) },
        { "violet", new Color(0.93f, 0.51f, 0.93f, 1f) },
        { "orange", new Color(1f, 0.65f, 0f, 1f) },
    };

    private static bool TryParseColorString(string str, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(str)) return false;

        str = str.Trim();

        if (NamedColors.TryGetValue(str, out color))
        {
            return true;
        }

        if (str.StartsWith("#"))
        {
            str = str.Substring(1);
        }

        if (str.Length == 3)
        {
            if (byte.TryParse(new string(str[0], 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(new string(str[1], 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(new string(str[2], 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                return true;
            }
        }
        else if (str.Length == 4)
        {
            if (byte.TryParse(new string(str[0], 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(new string(str[1], 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(new string(str[2], 2), System.Globalization.NumberStyles.HexNumber, null, out var b) &&
                byte.TryParse(new string(str[3], 2), System.Globalization.NumberStyles.HexNumber, null, out var a))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
        }
        else if (str.Length == 6)
        {
            if (byte.TryParse(str.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(str.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(str.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                return true;
            }
        }
        else if (str.Length == 8)
        {
            if (byte.TryParse(str.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(str.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(str.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b) &&
                byte.TryParse(str.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var a))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
        }

        return false;
    }

    private static Color ParseColorObject(string colorBlock)
    {
        var r = ReadFloat(colorBlock, "r");
        var g = ReadFloat(colorBlock, "g");
        var b = ReadFloat(colorBlock, "b");
        var hasA = Regex.IsMatch(colorBlock, "\"a\"\\s*:", RegexOptions.IgnoreCase);
        var a = hasA ? ReadFloat(colorBlock, "a") : (r > 1f || g > 1f || b > 1f ? 255f : 1f);

        // If any component is > 1.0, assume 0..255 scale
        if (r > 1f || g > 1f || b > 1f || a > 1f)
        {
            r /= 255f;
            g /= 255f;
            b /= 255f;
            a /= 255f;
        }

        return new Color(
            Mathf.Clamp01(r),
            Mathf.Clamp01(g),
            Mathf.Clamp01(b),
            Mathf.Clamp01(a));
    }

    // ---- Structural helpers -------------------------------------------------

    /// <summary>
    /// Extracts the raw string content of a JSON array property, e.g.
    /// <c>"objects": [ ... ]</c> returns the text between the brackets.
    /// </summary>
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
                    // Return content between '[' and ']'
                    return json.Substring(start + 1, i - start - 1);
                }
            }
        }

        return null; // Malformed JSON
    }

    /// <summary>
    /// Returns the content between the opening brace at <paramref name="openBraceIndex"/>
    /// and its matching closing brace (inclusive of surrounding braces).
    /// </summary>
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

    /// <summary>
    /// Splits an array body (text between [ and ]) into individual top-level
    /// <c>{ … }</c> blocks, handling nested objects correctly.
    /// </summary>
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
