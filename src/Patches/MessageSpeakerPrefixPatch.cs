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
/// Font-size enforcement is handled independently by <see cref="DialogueFontSizePatch"/>.
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
            string separator = Separator;
            try
            {
                var msgMgr = UnityEngine.Object.FindObjectOfType<MessageManager>();
                if (msgMgr != null && msgMgr.currentLanguage.ToString().Equals("Ja", StringComparison.OrdinalIgnoreCase))
                {
                    separator = "「";
                }
            }
            catch (Exception)
            {
                // Fallback silently if MessageManager is not yet initialized or ready
            }

            var prefix = speakerName + separator;

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

        // Apply the configured font size to both Text components via the independent
        // DialogueFontSizePatch helper — works regardless of MessageSpeakerPrefix state.
        if (DialogueFontSizePatch.TryGetTargetSize(out var targetSize))
        {
            DialogueFontSizePatch.ApplyFontSize(__instance, targetSize);
            DialogueFontSizePatch.ApplyFontSize(spekerText, targetSize);
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
