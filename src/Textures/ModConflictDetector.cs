using System;
using System.Collections.Generic;
using System.IO;
using KupoUI.PR;

namespace KupoUI.PR.Textures;

internal static class ModConflictDetector
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".dds"
    };

    private static readonly HashSet<string> KnownGameTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "FF1", "FF2", "FF3", "FF4", "FF5", "FF6"
    };

    private readonly struct TextureFileEntry
    {
        public string SourceName { get; }
        public string FilePath { get; }
        public string RelativePath { get; }

        public TextureFileEntry(string sourceName, string filePath, string relativePath)
        {
            SourceName = sourceName;
            FilePath = filePath;
            RelativePath = relativePath;
        }
    }

    internal static void Run(string rootPath, string currentGameTag)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        var modsPath = Path.Combine(rootPath, "00-Mods");
        var loadedMods = GetLoadedMods(modsPath);

        if (loadedMods.Count > 0)
        {
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[ModLoader] Loaded {loadedMods.Count} mod(s) from 00-Mods: {string.Join(", ", loadedMods)}");
        }
        else
        {
            KupoUIPRPlugin.PluginLog.LogInfo("[ModLoader] No mods found in 00-Mods.");
        }

        CheckTextureConflicts(rootPath, modsPath, currentGameTag);
    }

    private static List<string> GetLoadedMods(string modsPath)
    {
        var mods = new List<string>();
        if (!Directory.Exists(modsPath))
        {
            return mods;
        }

        foreach (var dir in Directory.GetDirectories(modsPath))
        {
            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName))
            {
                continue;
            }

            if (folderName.Equals("System", StringComparison.OrdinalIgnoreCase) || KnownGameTags.Contains(folderName))
            {
                continue;
            }

            mods.Add(folderName);
        }

        return mods;
    }

    private static void CheckTextureConflicts(string rootPath, string modsPath, string currentGameTag)
    {
        var nameKeyIndex = new Dictionary<string, List<TextureFileEntry>>(StringComparer.OrdinalIgnoreCase);
        var pathKeyIndex = new Dictionary<string, List<TextureFileEntry>>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan System layers
        var sharedFolders = new[]
        {
            Path.Combine(rootPath, "System"),
            Path.Combine(rootPath, "System", currentGameTag),
            Path.Combine(rootPath, currentGameTag),
            Path.Combine(rootPath, "00-Mods", "System"),
            Path.Combine(rootPath, "00-Mods", "System", currentGameTag),
            Path.Combine(rootPath, "00-Mods", currentGameTag)
        };

        foreach (var folder in sharedFolders)
        {
            ScanDirectoryForTextures("System", folder, rootPath, currentGameTag, nameKeyIndex, pathKeyIndex);
        }

        // 2. Scan Mod folders & root in 00-Mods
        if (Directory.Exists(modsPath))
        {
            // Scan direct files in 00-Mods root
            foreach (var file in Directory.EnumerateFiles(modsPath, "*.*", SearchOption.TopDirectoryOnly))
            {
                ProcessTextureFile("00-Mods (Root)", file, rootPath, currentGameTag, nameKeyIndex, pathKeyIndex);
            }

            // Scan subdirectories in 00-Mods
            foreach (var dir in Directory.GetDirectories(modsPath))
            {
                var folderName = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(folderName))
                {
                    continue;
                }

                if (folderName.Equals("System", StringComparison.OrdinalIgnoreCase) || KnownGameTags.Contains(folderName))
                {
                    continue;
                }

                var sourceName = $"Mod '{folderName}'";
                ScanDirectoryForTextures(sourceName, dir, rootPath, currentGameTag, nameKeyIndex, pathKeyIndex);
            }
        }

        // 3. Report Name Key Conflicts (only when textures come from multiple distinct sources e.g. Mod A vs Mod B, or Mod vs System)
        var reportedFileSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in nameKeyIndex)
        {
            var nameKey = kvp.Key;
            var entries = kvp.Value;

            if (!HasMultiSourceConflict(entries, out var distinctFiles))
            {
                continue;
            }

            var fileSetKey = BuildFileSetKey(distinctFiles);
            reportedFileSets.Add(fileSetKey);

            KupoUIPRPlugin.PluginLog.LogWarning($"[ModLoader] Texture conflict detected for '{nameKey}':");
            foreach (var entry in distinctFiles)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"  - [{entry.SourceName}] {entry.RelativePath}");
            }
        }

        // 4. Report Path Key Conflicts (addressable GameAssets paths across multiple distinct sources)
        foreach (var kvp in pathKeyIndex)
        {
            var pathKey = kvp.Key;
            var entries = kvp.Value;

            if (!HasMultiSourceConflict(entries, out var distinctFiles))
            {
                continue;
            }

            var fileSetKey = BuildFileSetKey(distinctFiles);
            if (reportedFileSets.Contains(fileSetKey))
            {
                continue;
            }

            reportedFileSets.Add(fileSetKey);

            KupoUIPRPlugin.PluginLog.LogWarning($"[ModLoader] Addressable path conflict detected for '{pathKey}':");
            foreach (var entry in distinctFiles)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"  - [{entry.SourceName}] {entry.RelativePath}");
            }
        }
    }

    private static void ScanDirectoryForTextures(
        string sourceName,
        string dirPath,
        string rootPath,
        string currentGameTag,
        Dictionary<string, List<TextureFileEntry>> nameKeyIndex,
        Dictionary<string, List<TextureFileEntry>> pathKeyIndex)
    {
        if (!Directory.Exists(dirPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dirPath, "*.*", SearchOption.AllDirectories))
        {
            ProcessTextureFile(sourceName, file, rootPath, currentGameTag, nameKeyIndex, pathKeyIndex);
        }
    }

    private static void ProcessTextureFile(
        string sourceName,
        string filePath,
        string rootPath,
        string currentGameTag,
        Dictionary<string, List<TextureFileEntry>> nameKeyIndex,
        Dictionary<string, List<TextureFileEntry>> pathKeyIndex)
    {
        var extension = Path.GetExtension(filePath);
        if (!SupportedExtensions.Contains(extension))
        {
            return;
        }

        var normalizedFile = filePath.Replace('\\', '/');
        var segments = normalizedFile.Split('/');

        foreach (var segment in segments)
        {
            if (segment.StartsWith("block", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (KnownGameTags.Contains(segment) && !segment.Equals(currentGameTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var relativePath = filePath.Substring(rootPath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var entry = new TextureFileEntry(sourceName, filePath, relativePath);

        // If the file is under GameAssets, key it strictly by its addressable GameAssets path key.
        // GameAssets folder uses full path resolution to prevent filename collisions across different folders.
        var pathKey = ExtractGameAssetsPathKey(rootPath, filePath);
        if (!string.IsNullOrEmpty(pathKey))
        {
            if (!pathKeyIndex.TryGetValue(pathKey, out var pathEntries))
            {
                pathEntries = new List<TextureFileEntry>();
                pathKeyIndex[pathKey] = pathEntries;
            }
            pathEntries.Add(entry);
        }
        else
        {
            // For loose (non-GameAssets) textures, key by filename without extension.
            var nameKey = Path.GetFileNameWithoutExtension(filePath).Trim();
            if (!string.IsNullOrEmpty(nameKey))
            {
                if (!nameKeyIndex.TryGetValue(nameKey, out var nameEntries))
                {
                    nameEntries = new List<TextureFileEntry>();
                    nameKeyIndex[nameKey] = nameEntries;
                }
                nameEntries.Add(entry);
            }
        }
    }

    private static string ExtractGameAssetsPathKey(string rootPath, string filePath)
    {
        var relative = filePath.Substring(rootPath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

        const string marker = "GameAssets/";
        var idx = relative.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            if (!relative.StartsWith("GameAssets", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            idx = 0;
        }

        var pathKey = relative.Substring(idx);
        var lastSlash = pathKey.LastIndexOf('/');
        var extDot = pathKey.LastIndexOf('.');
        if (extDot > lastSlash)
        {
            pathKey = pathKey.Substring(0, extDot);
        }

        return pathKey;
    }

    private static bool HasMultiSourceConflict(List<TextureFileEntry> entries, out List<TextureFileEntry> distinctFiles)
    {
        distinctFiles = GetDistinctFileEntries(entries);
        if (distinctFiles.Count <= 1)
        {
            return false;
        }

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in distinctFiles)
        {
            sources.Add(file.SourceName);
        }

        return sources.Count > 1;
    }

    private static List<TextureFileEntry> GetDistinctFileEntries(List<TextureFileEntry> entries)
    {
        var distinct = new List<TextureFileEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (seenPaths.Add(entry.FilePath))
            {
                distinct.Add(entry);
            }
        }

        return distinct;
    }

    private static string BuildFileSetKey(List<TextureFileEntry> entries)
    {
        var paths = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            paths.Add(entry.FilePath.Replace('\\', '/').ToLowerInvariant());
        }
        paths.Sort(StringComparer.Ordinal);
        return string.Join("|", paths);
    }
}
