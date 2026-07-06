using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using Last.Message;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Handles dynamically injecting and displaying speaker portraits inside the message window.
/// </summary>
[HarmonyPatch]
internal static class SpeakerPortraitsPatch
{
    private static readonly Dictionary<string, Sprite> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<string> _cachedFolders;

    static SpeakerPortraitsPatch()
    {
        GetOrCreateDefaultFolder();
    }

    internal static void ClearCache()
    {
        _portraitCache.Clear();
        _cachedFolders = null;
    }

    private static int GetFolderPriority(string path, string root)
    {
        string normRoot = root.Replace('\\', '/').TrimEnd('/');
        string normPath = path.Replace('\\', '/').TrimEnd('/');

        if (!normPath.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        string relative = normPath.Substring(normRoot.Length).TrimStart('/');
        string[] parts = relative.Split('/');

        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
        {
            return 0;
        }

        string layer = parts[0];
        string gameTag = Textures.TextureResolver.CurrentGameTag;

        if (layer.Equals("05-Button-Prompts", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1 && parts[1].Equals(Textures.TextureResolver.ButtonPromptsPack, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }
            return -1;
        }

        if (layer.Equals("04-UI-Cursors", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1 && parts[1].Equals(Textures.TextureResolver.CursorsPack, StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }
            return -1;
        }

        if (layer.Equals("03-UI-BgColor", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1 && parts[1].Equals(Textures.TextureResolver.UiBgColorPack, StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }
            return -1;
        }

        if (layer.Equals("02-UI-Frames", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1 && parts[1].Equals(Textures.TextureResolver.UiFramesPack, StringComparison.OrdinalIgnoreCase))
            {
                return 70;
            }
            return -1;
        }

        if (layer.Equals("01-UI-Themes", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1 && parts[1].Equals(Textures.TextureResolver.UiThemesPack, StringComparison.OrdinalIgnoreCase))
            {
                return 60;
            }
            return -1;
        }

        if (layer.Equals("00-Mods", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1)
            {
                string nextSegment = parts[1];
                if (nextSegment.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    return 30;
                }
                if (nextSegment.Equals("Shared", StringComparison.OrdinalIgnoreCase))
                {
                    return 20;
                }
                if (nextSegment.Equals("SpeakerPortraits", StringComparison.OrdinalIgnoreCase))
                {
                    return 50;
                }
                return 40;
            }
            return 50;
        }

        if (layer.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (layer.Equals("Shared", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length > 1)
            {
                string nextSegment = parts[1];
                if (nextSegment.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    return 8;
                }
                if (nextSegment.Equals("FF1", StringComparison.OrdinalIgnoreCase)
                    || nextSegment.Equals("FF2", StringComparison.OrdinalIgnoreCase)
                    || nextSegment.Equals("FF3", StringComparison.OrdinalIgnoreCase)
                    || nextSegment.Equals("FF4", StringComparison.OrdinalIgnoreCase)
                    || nextSegment.Equals("FF5", StringComparison.OrdinalIgnoreCase)
                    || nextSegment.Equals("FF6", StringComparison.OrdinalIgnoreCase))
                {
                    return -1;
                }
            }
            return 5;
        }

        return 0;
    }

    /// <summary>
    /// Scans the {GameRoot}/Modules/ directory for any subfolders named "SpeakerPortraits".
    /// </summary>
    private static List<string> GetSpeakerPortraitFolders()
    {
        if (_cachedFolders != null)
        {
            return _cachedFolders;
        }

        var folders = new List<string>();
        string root = KupoUIPRPlugin.ModulesRootPath;
        if (!Directory.Exists(root))
        {
            return folders;
        }

        try
        {
            var sortedFolders = new List<(string Path, int Priority)>();

            if (Path.GetFileName(root).Equals("SpeakerPortraits", StringComparison.OrdinalIgnoreCase))
            {
                int priority = GetFolderPriority(root, root);
                if (priority >= 0)
                {
                    sortedFolders.Add((root, priority));
                }
            }

            var matches = Directory.GetDirectories(root, "SpeakerPortraits", SearchOption.AllDirectories);
            foreach (var match in matches)
            {
                int priority = GetFolderPriority(match, root);
                if (priority >= 0)
                {
                    sortedFolders.Add((match, priority));
                }
            }

            sortedFolders.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var item in sortedFolders)
            {
                folders.Add(item.Path);
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogError($"[SpeakerPortraits] Error scanning directories: {ex.Message}");
        }

        if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] Scanned portrait folders: {string.Join(", ", folders)}");
        }

        _cachedFolders = folders;
        return folders;
    }

    /// <summary>
    /// Auto-creates the default folder under {GameRoot}/Modules/Shared/SpeakerPortraits if none exist.
    /// </summary>
    private static string GetOrCreateDefaultFolder()
    {
        string defaultPath = Path.Combine(KupoUIPRPlugin.ModulesRootPath, "Shared", "SpeakerPortraits");
        if (!Directory.Exists(defaultPath))
        {
            try
            {
                Directory.CreateDirectory(defaultPath);
                KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] Created default directory at: {defaultPath}");
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[SpeakerPortraits] Failed to create default directory: {ex.Message}");
            }
        }
        return defaultPath;
    }

    private static string FindPortraitFile(string speakerId, string speakerName)
    {
        // Ensure the default directory exists so players know where to drop images
        GetOrCreateDefaultFolder();

        var folders = GetSpeakerPortraitFolders();

        // 1. Check for <SpeakerID>.png (stable & precise)
        if (!string.IsNullOrEmpty(speakerId) && !speakerId.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var folder in folders)
            {
                string match = FindFileRecursive(folder, speakerId + ".png");
                if (match != null)
                {
                    return match;
                }
            }
        }

        // 2. Check for <SpeakerName>.png (user-friendly name match)
        if (!string.IsNullOrWhiteSpace(speakerName))
        {
            string sanitizedName = SanitizeFileName(speakerName);
            if (!string.IsNullOrEmpty(sanitizedName))
            {
                foreach (var folder in folders)
                {
                    string match = FindFileRecursive(folder, sanitizedName + ".png");
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
        }

        return null;
    }

    private static string FindFileRecursive(string dir, string fileName)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        try
        {
            var files = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files[0];
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogError($"[SpeakerPortraits] Error scanning recursively in '{dir}': {ex.Message}");
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    /// <summary>
    /// Loads a PNG from the filesystem into a Unity Sprite, utilizing caching and applying sidecar JSON metadata if present.
    /// </summary>
    private static Sprite GetOrCreatePortraitSprite(string filePath)
    {
        if (_portraitCache.TryGetValue(filePath, out var cachedSprite) && cachedSprite != null)
        {
            return cachedSprite;
        }

        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, data))
            {
                // Read sidecar metadata if available
                var metadata = Textures.TextureResolver.LoadTextureMetadata(filePath);

                // Apply filter mode and wrap mode from metadata/defaults
                texture.filterMode = Textures.TextureResolver.ResolveFilterMode(filePath, metadata);
                texture.wrapMode = Textures.TextureResolver.ResolveWrapMode(filePath, metadata);

                // Parse pivot (default 0.5, 0.5)
                var pivot = new Vector2(0.5f, 0.5f);
                var parsedPivot = Textures.TextureResolver.ParsePivot(metadata);
                if (parsedPivot.HasValue)
                {
                    pivot = parsedPivot.Value;
                }

                // Parse PPU (default 100)
                float ppu = 100f;
                if (metadata != null && metadata.PixelsPerUnit > 0f)
                {
                    ppu = metadata.PixelsPerUnit;
                }

                // Parse 9-slice borders (default Vector4.zero)
                var border = Vector4.zero;
                var parsedBorder = Textures.TextureResolver.ParseBorder(metadata);
                if (parsedBorder.HasValue)
                {
                    border = parsedBorder.Value;
                }

                var rect = new Rect(0, 0, texture.width, texture.height);
                var sprite = Sprite.Create(texture, rect, pivot, ppu, 0, SpriteMeshType.FullRect, border);

                texture.hideFlags |= HideFlags.DontSave;
                sprite.hideFlags |= HideFlags.DontSave;

                if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[SpeakerPortraits] Created sprite for {Path.GetFileName(filePath)}: " +
                        $"filter={texture.filterMode}, ppu={ppu}, pivot={pivot}, border={border}");
                }

                _portraitCache[filePath] = sprite;
                return sprite;
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogError($"[SpeakerPortraits] Failed to load portrait sprite from '{filePath}': {ex.Message}");
        }

        return null;
    }



    private static void InjectPortrait(MessageWindowView view, string speakerId, string speakerName, string imagePath)
    {
        if (view == null) return;

        var parent = view.transform.Find("message_root/message_root/root");
        if (parent == null)
        {
            if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogWarning("[SpeakerPortraits] Could not find target path 'message_root/message_root/root' relative to MessageWindowView.");
            }
            return;
        }

        // Find battle window frame and last_text to apply overrides
        var battleWindow = view.transform.Find("message_root/common_battlewindow");
        var lastText = view.transform.Find("message_root/message_root/root/last_text");

        if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] InjectPortrait: speakerId='{speakerId}', speakerName='{speakerName ?? "null"}', imagePath='{imagePath ?? "null"}'");
        }

        string targetName = "Portrait_" + speakerId;

        if (string.IsNullOrEmpty(imagePath))
        {
            // Reset to defaults directly
            if (battleWindow != null)
            {
                battleWindow.localScale = Vector3.one;
                if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Reset battleWindow scale to (1.0, 1.0, 1.0)");
            }
            if (lastText != null)
            {
                lastText.localPosition = Vector3.zero;
                if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Reset lastText position to (0.0, 0.0, 0.0)");
            }

            // Set all custom portraits to inactive
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("Portrait_", StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                }
            }
            return;
        }

        // Set all other portraits to inactive, and only keep the target speaker portrait active (if it already exists)
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith("Portrait_", StringComparison.Ordinal))
            {
                child.gameObject.SetActive(child.name == targetName);
            }
        }

        var existingPortrait = parent.Find(targetName);
        if (existingPortrait != null)
        {
            // Apply dialogue-box sizing adjustments to fit the portrait area
            if (battleWindow != null)
            {
                battleWindow.localScale = new Vector3(1.2f, 1f, 1f);
            }
            if (lastText != null)
            {
                lastText.localPosition = new Vector3(129.5999f, 0f, 0f);
            }
            return;
        }

        var sprite = GetOrCreatePortraitSprite(imagePath);
        if (sprite == null)
        {
            // Reset to defaults directly if sprite failed to load
            if (battleWindow != null) battleWindow.localScale = Vector3.one;
            if (lastText != null) lastText.localPosition = Vector3.zero;

            // Set all custom portraits to inactive
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("Portrait_", StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                }
            }
            return;
        }

        // Apply dialogue-box sizing adjustments to fit the portrait area
        if (battleWindow != null)
        {
            battleWindow.localScale = new Vector3(1.2f, 1f, 1f);
            if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Set battleWindow scale to 1.2");
        }
        if (lastText != null)
        {
            lastText.localPosition = new Vector3(129.5999f, 0f, 0f);
            if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Set lastText position to 129.5999");
        }

        if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] Injecting portrait for '{speakerId}' ({speakerName ?? "null"}) from '{imagePath}'");
        }

        var portraitGo = new GameObject(targetName);
        portraitGo.transform.SetParent(parent, false);

        var image = portraitGo.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;

        var rectTransform = portraitGo.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            rectTransform.sizeDelta = new Vector2(256f, 256f);
            rectTransform.localPosition = new Vector3(-522f, 0f, 0f);
            rectTransform.localScale = Vector3.one;
        }
    }

    /// <summary>
    /// Destroys all portrait game objects inside the view.
    /// </summary>
    private static void ClearPortraits(MessageWindowView view)
    {
        if (view == null) return;
        var parent = view.transform.Find("message_root/message_root/root");
        if (parent != null)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("Portrait_", StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        var battleWindow = view.transform.Find("message_root/common_battlewindow");
        if (battleWindow != null) battleWindow.localScale = Vector3.one;

        var lastText = view.transform.Find("message_root/message_root/root/last_text");
        if (lastText != null) lastText.localPosition = Vector3.zero;
    }

    // ── HARMONY HOOKS ────────────────────────────────────────────────────

    [HarmonyPatch(typeof(MessageWindowView), nameof(MessageWindowView.Initialize))]
    [HarmonyPostfix]
    private static void InitializePostfix(MessageWindowView __instance)
    {
        ClearPortraits(__instance);
    }

    [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
    [HarmonyPostfix]
    private static void TextSetterPostfix(Text __instance, string value)
    {
        if (!KupoUIPRPlugin.EnableSpeakerPortraitsConfig.Value)
        {
            return;
        }

        var view = FindMessageWindowView(__instance);
        if (view == null) return;

        var msgText = view.messageText;
        if (msgText == null || msgText.Pointer != __instance.Pointer) return;

        string speakerId = MessageSpeakerPrefixPatch.LastSpeakerID;
        string speakerName = view.spekerText != null ? view.spekerText.text : null;

        string imagePath = FindPortraitFile(speakerId, speakerName);

        InjectPortrait(view, speakerId, speakerName, imagePath);
    }

    private static MessageWindowView FindMessageWindowView(Text text)
    {
        if (text == null || text.gameObject == null)
        {
            return null;
        }

        var current = text.gameObject.transform;
        const int maxDepth = 8;
        for (var depth = 0; depth < maxDepth && current != null; depth++)
        {
            var view = current.gameObject.GetComponent<MessageWindowView>();
            if (view != null)
            {
                return view;
            }

            current = current.parent;
        }

        return null;
    }
}
