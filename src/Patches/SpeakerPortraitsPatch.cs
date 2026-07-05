using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    private static readonly ConditionalWeakTable<MessageWindowView, PortraitLayoutData> _layoutTrackers = new();
    private static List<string> _cachedFolders;

    static SpeakerPortraitsPatch()
    {
        GetOrCreateDefaultFolder();
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
            if (Path.GetFileName(root).Equals("SpeakerPortraits", StringComparison.OrdinalIgnoreCase))
            {
                folders.Add(root);
            }

            foreach (var subDir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(subDir);
                if (name.Equals("SpeakerPortraits", StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(subDir);
                }
                else if (name.Equals("00-Mods", StringComparison.OrdinalIgnoreCase))
                {
                    string directTarget = Path.Combine(subDir, "SpeakerPortraits");
                    if (Directory.Exists(directTarget))
                    {
                        folders.Add(directTarget);
                    }

                    foreach (var modDir in Directory.GetDirectories(subDir))
                    {
                        string modName = Path.GetFileName(modDir);
                        if (!modName.Equals("SpeakerPortraits", StringComparison.OrdinalIgnoreCase))
                        {
                            string target = Path.Combine(modDir, "SpeakerPortraits");
                            if (Directory.Exists(target))
                            {
                                folders.Add(target);
                            }
                        }
                    }
                }
                else
                {
                    string target = Path.Combine(subDir, "SpeakerPortraits");
                    if (Directory.Exists(target))
                    {
                        folders.Add(target);
                    }
                }
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
    /// Auto-creates the default folder under {GameRoot}/Modules/00-Mods/SpeakerPortraits if none exist.
    /// </summary>
    private static string GetOrCreateDefaultFolder()
    {
        string defaultPath = Path.Combine(KupoUIPRPlugin.ModulesRootPath, "00-Mods", "SpeakerPortraits");
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
    /// Loads a PNG from the filesystem into a Unity Sprite, utilizing caching and applying point filtering if inside a Pixel folder.
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
                if (Textures.TextureResolver.ShouldUsePointFilter(filePath))
                {
                    texture.filterMode = FilterMode.Point;
                    if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] Applied Point Filter to portrait: {Path.GetFileName(filePath)}");
                }
                else
                {
                    texture.filterMode = FilterMode.Bilinear;
                }

                var rect = new Rect(0, 0, texture.width, texture.height);
                var pivot = new Vector2(0.5f, 0.5f);
                var sprite = Sprite.Create(texture, rect, pivot);

                texture.hideFlags |= HideFlags.DontSave;
                sprite.hideFlags |= HideFlags.DontSave;

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

        // Always clean up existing custom portrait children
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith("Portrait_", StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // Find battle window frame and last_text to apply overrides
        var battleWindow = view.transform.Find("message_root/common_battlewindow");
        var lastText = view.transform.Find("message_root/message_root/root/last_text");

        // Get or create the tracker data association
        if (!_layoutTrackers.TryGetValue(view, out var tracker))
        {
            tracker = new PortraitLayoutData();
            _layoutTrackers.Add(view, tracker);
        }

        if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits] InjectPortrait: speakerId='{speakerId}', speakerName='{speakerName ?? "null"}', imagePath='{imagePath ?? "null"}'");
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits]   battleWindow: {(battleWindow != null ? "FOUND scale=" + battleWindow.localScale.ToString() : "NULL")}");
            KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits]   lastText: {(lastText != null ? "FOUND pos=" + lastText.localPosition.ToString() : "NULL")}");
        }

        if (string.IsNullOrEmpty(imagePath))
        {
            // Reset to default captured transforms ONLY if we previously modified them
            if (tracker.hasSaved)
            {
                if (battleWindow != null)
                {
                    battleWindow.localScale = tracker.originalBattleWindowScale;
                    if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Reset battleWindow scale to " + tracker.originalBattleWindowScale.ToString());
                }
                if (lastText != null)
                {
                    lastText.localPosition = tracker.originalLastTextPosition;
                    if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value) KupoUIPRPlugin.PluginLog.LogInfo("[SpeakerPortraits]   Reset lastText position to " + tracker.originalLastTextPosition.ToString());
                }
            }
            return;
        }

        // We are about to apply portrait modifications. Save the true default layout parameters now, if not saved already.
        if (!tracker.hasSaved)
        {
            if (battleWindow != null) tracker.originalBattleWindowScale = battleWindow.localScale;
            if (lastText != null) tracker.originalLastTextPosition = lastText.localPosition;
            tracker.hasSaved = true;
            if (KupoUIPRPlugin.EnablePortraitLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[SpeakerPortraits]   Saved defaults: scale={tracker.originalBattleWindowScale}, pos={tracker.originalLastTextPosition}");
            }
        }

        var sprite = GetOrCreatePortraitSprite(imagePath);
        if (sprite == null)
        {
            // Reset to defaults if sprite failed to load
            if (battleWindow != null)
            {
                battleWindow.localScale = tracker.originalBattleWindowScale;
            }
            if (lastText != null)
            {
                lastText.localPosition = tracker.originalLastTextPosition;
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

        var portraitGo = new GameObject("Portrait_" + speakerId);
        portraitGo.transform.SetParent(parent, false);

        var image = portraitGo.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;

        var rectTransform = portraitGo.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(KupoUIPRPlugin.PortraitAnchorMinXConfig.Value, KupoUIPRPlugin.PortraitAnchorMinYConfig.Value);
            rectTransform.anchorMax = new Vector2(KupoUIPRPlugin.PortraitAnchorMaxXConfig.Value, KupoUIPRPlugin.PortraitAnchorMaxYConfig.Value);
            rectTransform.pivot = new Vector2(KupoUIPRPlugin.PortraitPivotXConfig.Value, KupoUIPRPlugin.PortraitPivotYConfig.Value);

            rectTransform.sizeDelta = new Vector2(KupoUIPRPlugin.PortraitWidthConfig.Value, KupoUIPRPlugin.PortraitHeightConfig.Value);
            rectTransform.anchoredPosition = new Vector2(KupoUIPRPlugin.PortraitOffsetXConfig.Value, KupoUIPRPlugin.PortraitOffsetYConfig.Value);
            rectTransform.localScale = new Vector3(KupoUIPRPlugin.PortraitScaleXConfig.Value, KupoUIPRPlugin.PortraitScaleYConfig.Value, 1f);
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
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        if (_layoutTrackers.TryGetValue(view, out var tracker) && tracker.hasSaved)
        {
            var battleWindow = view.transform.Find("message_root/common_battlewindow");
            if (battleWindow != null)
            {
                battleWindow.localScale = tracker.originalBattleWindowScale;
            }
            var lastText = view.transform.Find("message_root/message_root/root/last_text");
            if (lastText != null)
            {
                lastText.localPosition = tracker.originalLastTextPosition;
            }
        }
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

/// <summary>
/// A temporary data holder associated with MessageWindowView to track and preserve the default UI positions.
/// </summary>
internal class PortraitLayoutData
{
    public Vector3 originalLastTextPosition;
    public Vector3 originalBattleWindowScale;
    public bool hasSaved = false;
}
