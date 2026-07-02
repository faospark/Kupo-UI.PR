using System;
using HarmonyLib;
using Last.Management;
using Last.Message;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Intercepts PlayMessageCommon and MessageManager.GetMessage to capture
/// the internal dialogue ID/Key (e.g. "E0001_00_999_a_01"), and hooks
/// Text.text setter to prepend the speaker name and log the active dialogue ID.
/// Also forces the custom font size and locks it during layout / rendering.
/// </summary>
[HarmonyPatch]
internal static class MessageSpeakerPrefixPatch
{
    private const string Separator = ": ";

    /// <summary>
    /// Stores the last dialogue ID captured from the game's event interpreter or localization manager.
    /// </summary>
    public static string LastDialogueID { get; private set; } = "None";

    /// <summary>
    /// Reentrancy guard: set to <c>true</c> while we are rewriting <c>value</c>
    /// so that the setter we trigger on ourselves does not loop.
    /// </summary>
#pragma warning disable CS0649
    [ThreadStatic]
    private static bool _isApplying;
#pragma warning restore CS0649

    // ── FORCE BEST FIT TO REMAIN FALSE FOR DIALOGUE TEXT ─────────────────
    [HarmonyPatch(typeof(Text), nameof(Text.resizeTextForBestFit), MethodType.Setter)]
    [HarmonyPrefix]
    private static void ResizeTextForBestFitSetterPrefix(Text __instance, ref bool value)
    {
        if (!KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value)
        {
            return;
        }

        var name = __instance.name;
        if (name == "message_text" && IsInDialogueWindow(__instance.transform))
        {
            value = false; // Force it to remain false
        }
    }

    // ── FORCE FONT SIZE ON DIALOGUE AND SPEAKER TEXTS ─────────────────────
    [HarmonyPatch(typeof(Text), nameof(Text.fontSize), MethodType.Setter)]
    [HarmonyPrefix]
    private static void FontSizeSetterPrefix(Text __instance, ref int value)
    {
        if (!KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value)
        {
            return;
        }

        var fontSizeRaw = KupoUIPRPlugin.MessageSpeakerPrefixFontSizeConfig.Value;
        if (!string.IsNullOrWhiteSpace(fontSizeRaw)
            && !fontSizeRaw.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fontSizeRaw.Trim(), out var targetSize)
            && targetSize > 0)
        {
            var name = __instance.name;
            if ((name == "message_text" || name == "speker_text") && IsInDialogueWindow(__instance.transform))
            {
                value = targetSize; // Force it to our target size
            }
        }
    }

    // ── OVERRIDE SERIALIZATION RESETS VIA GRAPHIC HOOKS ────────────────────
    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    [HarmonyPostfix]
    private static void GraphicOnEnablePostfix(Graphic __instance)
    {
        if (IsDialogueText(__instance, out var textComp, out var targetSize))
        {
            if (textComp.fontSize != targetSize)
            {
                textComp.fontSize = targetSize;
            }
            if (textComp.resizeTextForBestFit)
            {
                textComp.resizeTextForBestFit = false;
            }
        }
    }

    [HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
    [HarmonyPostfix]
    private static void GraphicUpdateMaterialPostfix(Graphic __instance)
    {
        if (IsDialogueText(__instance, out var textComp, out var targetSize))
        {
            if (textComp.fontSize != targetSize)
            {
                textComp.fontSize = targetSize;
            }
            if (textComp.resizeTextForBestFit)
            {
                textComp.resizeTextForBestFit = false;
            }
        }
    }

    private static bool IsDialogueText(Graphic graphic, out Text textComp, out int targetSize)
    {
        textComp = null;
        targetSize = 0;

        if (graphic == null || graphic.gameObject == null)
        {
            return false;
        }

        textComp = graphic.GetComponent<Text>();
        if (textComp == null)
        {
            return false;
        }

        var name = graphic.name;
        if (name != "message_text" && name != "speker_text")
        {
            return false;
        }

        if (!KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value)
        {
            return false;
        }

        var fontSizeRaw = KupoUIPRPlugin.MessageSpeakerPrefixFontSizeConfig.Value;
        if (string.IsNullOrWhiteSpace(fontSizeRaw)
            || fontSizeRaw.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(fontSizeRaw.Trim(), out targetSize)
            || targetSize <= 0)
        {
            return false;
        }

        return IsInDialogueWindow(graphic.transform);
    }

    private static bool IsInDialogueWindow(UnityEngine.Transform t)
    {
        if (t == null) return false;

        var current = t;
        const int maxDepth = 8;
        for (var depth = 0; depth < maxDepth && current != null; depth++)
        {
            var name = current.name;
            if (name.Contains("message_window") || name.Contains("message_parent") || name.Contains("upper_parent") || name.Contains("lower_parent"))
            {
                return true;
            }
            current = current.parent;
        }

        return false;
    }

    // ── INTERCEPT PLAYMESSAGECOMMON TO CAPTURE DIALOGUE ID ────────────────
    [HarmonyPatch(typeof(Last.Interpreter.Instructions.Message), "PlayMessageCommon")]
    [HarmonyPrefix]
    private static void PlayMessageCommonPrefix(string messageID)
    {
        LastDialogueID = messageID;
    }

    // ── INTERCEPT GETMESSAGE TO CAPTURE LOCALIZATION ID ──────────────────
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetMessage), new[] { typeof(string), typeof(bool) })]
    [HarmonyPrefix]
    private static void GetMessagePrefix(string key, bool isReplace)
    {
        // Dialogue and menu keys in FFPR typically start with prefixes like 'E' (event), 'M' (menu), 'S' (system), etc.
        // Caching the last requested key allows us to bind it when Text.text is set immediately after.
        if (!string.IsNullOrEmpty(key))
        {
            LastDialogueID = key;
        }
    }

    // ── INTERCEPT TEXT SETTER TO LOG DETAILS AND PREPEND SPEAKER ─────────
    [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
    [HarmonyPrefix]
    private static void TextSetterPrefix(Text __instance, ref string value)
    {
        if (_isApplying)
        {
            return;
        }

        if (!KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value)
        {
            return;
        }

        // Only act on non-empty messages.
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // Walk up the hierarchy looking for a MessageWindowView.
        var view = FindMessageWindowView(__instance);
        if (view == null)
        {
            return; // Not a message text we care about.
        }

        // Confirm this Text IS the messageText field (not spekerText itself).
        var msgText = view.messageText;
        if (msgText == null || msgText.Pointer != __instance.Pointer)
        {
            return;
        }

        // Get the speaker name.
        var spekerText = view.spekerText;
        var speakerName = spekerText != null ? spekerText.text : null;

        // Log speakerText and messageText IDs (both Instance ID and Native Pointer) along with the Dialogue Key
        int msgTextId = __instance.GetInstanceID();
        string msgTextPtr = __instance.Pointer.ToString("X");
        int speakerTextId = spekerText != null ? spekerText.GetInstanceID() : 0;
        string speakerTextPtr = spekerText != null ? spekerText.Pointer.ToString("X") : "null";

        if (KupoUIPRPlugin.MessageSpeakerPrefixLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[MessageSpeakerPrefix] Dialogue matched. " +
                $"Key: '{LastDialogueID}', " +
                $"MessageText ID: {msgTextId} (Ptr: {msgTextPtr}), " +
                $"SpeakerText ID: {speakerTextId} (Ptr: {speakerTextPtr}), " +
                $"SpeakerName: '{speakerName ?? "(null)"}', " +
                $"Message: '{value}'");
        }

        if (!string.IsNullOrWhiteSpace(speakerName))
        {
            var prefix = speakerName + Separator;

            // Guard against double-prefix if this fires twice.
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (KupoUIPRPlugin.MessageSpeakerPrefixLoggingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[MessageSpeakerPrefix] Prepending speaker '{speakerName}' to message '{value}'");
                }

                _isApplying = true;
                try
                {
                    value = prefix + value;
                }
                finally
                {
                    _isApplying = false;
                }
            }
        }

        // Apply the configured font size to both Text components, unless
        // the user left the setting as "Auto" (meaning: don't touch it).
        var fontSizeRaw = KupoUIPRPlugin.MessageSpeakerPrefixFontSizeConfig.Value;
        if (!string.IsNullOrWhiteSpace(fontSizeRaw)
            && !fontSizeRaw.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fontSizeRaw.Trim(), out var targetSize)
            && targetSize > 0)
        {
            ApplyFontSize(__instance, targetSize);
            ApplyFontSize(spekerText, targetSize);
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    private static void ApplyFontSize(Text text, int size)
    {
        if (text == null)
        {
            return;
        }

        if (text.fontSize != size)
        {
            text.fontSize = size;
            KupoUIPRPlugin.PluginLog.LogDebug(
                $"[MessageSpeakerPrefix] fontSize set to {size} on '{text.name}'.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks from <paramref name="text"/>'s GameObject up the parent chain
    /// looking for a <see cref="MessageWindowView"/> component.
    /// </summary>
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
