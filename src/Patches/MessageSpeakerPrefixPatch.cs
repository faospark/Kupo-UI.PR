using System;
using System.Text;
using System.Runtime.CompilerServices;
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

    private class MessageWindowState
    {
        public string DialogueId = "None";
        public string SpeakerId = "None";
        public string SpeakerName;
    }

    private static readonly ConditionalWeakTable<MessageWindowView, MessageWindowState> _viewStates = new();

    /// <summary>
    /// Stores the last dialogue ID captured from the game's event interpreter or localization manager.
    /// </summary>
    public static string LastDialogueID { get; private set; } = "None";

    /// <summary>
    /// Stores the last speaker ID captured from the localization manager.
    /// </summary>
    public static string LastSpeakerID { get; private set; } = "None";

    private static bool IsDialogueKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 2 || key.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        char firstChar = char.ToUpperInvariant(key[0]);
        if (char.IsLetter(firstChar))
        {
            char secondChar = key[1];
            return char.IsDigit(secondChar) || secondChar == '_';
        }
        return false;
    }

    internal static void ResetWindowState(MessageWindowView view)
    {
        if (view != null && _viewStates.TryGetValue(view, out var state))
        {
            state.DialogueId = "None";
            state.SpeakerId = "None";
            state.SpeakerName = null;
        }
    }

    internal static void GetDialogueContext(MessageWindowView view, out string speakerId, out string speakerName, out string dialogueId)
    {
        var state = _viewStates.GetOrCreateValue(view);

        var viewSpeakerText = view != null && view.spekerText != null ? view.spekerText.text : null;

        // Since LastDialogueID and LastSpeakerID only track dialogue keys, they are stable throughout the active dialogue.
        if (IsDialogueKey(LastDialogueID))
        {
            state.DialogueId = LastDialogueID;

            // Update SpeakerId only if a valid speaker ID is returned or if we need to fall back
            if (!string.IsNullOrEmpty(LastSpeakerID) && !LastSpeakerID.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                state.SpeakerId = LastSpeakerID;
            }
            else if (string.IsNullOrWhiteSpace(viewSpeakerText) || !string.Equals(state.SpeakerName, viewSpeakerText, StringComparison.OrdinalIgnoreCase))
            {
                state.SpeakerId = "None";
            }

            state.SpeakerName = viewSpeakerText;
        }

        speakerId = state.SpeakerId ?? "None";
        speakerName = !string.IsNullOrWhiteSpace(viewSpeakerText) ? viewSpeakerText : state.SpeakerName;
        dialogueId = state.DialogueId ?? "None";

        // Priority 1 — message-specific override (most precise, beats everything else).
        if (KupoUIPRPlugin.TryGetMessageOverride(dialogueId, out var msgSpeakerId, out var msgSpeakerName))
        {
            if (!string.IsNullOrEmpty(msgSpeakerId)) speakerId = msgSpeakerId;
            if (!string.IsNullOrEmpty(msgSpeakerName)) speakerName = msgSpeakerName;
        }
        // Priority 2 — speaker-ID registration (always applied when the speaker ID is registered).
        else if (KupoUIPRPlugin.TryGetSpeakerNameOverride(speakerId, out var nameOverride))
        {
            speakerName = nameOverride;
        }
    }

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
        LastSpeakerID = "None";
    }

    // ── INTERCEPT GETMESSAGE TO CAPTURE LOCALIZATION ID ──────────────────
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetMessage), new[] { typeof(string), typeof(bool) })]
    [HarmonyPrefix]
    private static void GetMessagePrefix(string key, bool isReplace)
    {
        // Dialogue keys in FFPR typically start with prefixes like 'E' (event), 'B' (battle), 'Q' (quest), 'S' (system), etc.
        // We only capture these keys to avoid background UI/menu string lookups from overwriting the active dialogue context.
        if (IsDialogueKey(key))
        {
            LastDialogueID = key;
            LastSpeakerID = "None";
        }
    }

    // ── INTERCEPT GETSPEAKERMESSAGEID TO CAPTURE SPEAKER ID ────────────────
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetSpeakerMessageId), new[] { typeof(string) })]
    [HarmonyPostfix]
    private static void GetSpeakerMessageIdPostfix(string __result)
    {
        if (!string.IsNullOrEmpty(__result))
        {
            LastSpeakerID = __result;
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

        if (!KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value && !KupoUIPRPlugin.DiagnosticMessageSpeakerPrefixLoggingConfig.Value)
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

        // Get effective speaker ID, speaker name, and dialogue ID for this view context.
        GetDialogueContext(view, out var effectiveSpeakerId, out var speakerName, out var effectiveDialogueId);
        var spekerText = view.spekerText;

        // Log speakerText and messageText IDs (both Instance ID and Native Pointer) along with the Dialogue Key
        int msgTextId = __instance.GetInstanceID();
        string msgTextPtr = __instance.Pointer.ToString("X");
        int speakerTextId = spekerText != null ? spekerText.GetInstanceID() : 0;
        string speakerTextPtr = spekerText != null ? spekerText.Pointer.ToString("X") : "null";

        if (KupoUIPRPlugin.DiagnosticMessageSpeakerPrefixLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[MessageSpeakerPrefix] Dialogue matched. " +
                $"Key: '{effectiveDialogueId}', " +
                $"SpeakerID: '{effectiveSpeakerId}', " +
                $"MessageText ID: {msgTextId} (Ptr: {msgTextPtr}), " +
                $"SpeakerText ID: {speakerTextId} (Ptr: {speakerTextPtr}), " +
                $"SpeakerName: '{speakerName ?? "(null)"}', " +
                $"Message: '{value}'");
        }

        if (KupoUIPRPlugin.MessageSpeakerPrefixConfig.Value && !string.IsNullOrWhiteSpace(speakerName))
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

            if (KupoUIPRPlugin.SpeakerNameUppercaseConfig.Value)
            {
                speakerName = speakerName.ToUpperInvariant();
            }

            var prefix = speakerName + separator;

            // Guard against double-prefix if this fires twice.
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (KupoUIPRPlugin.DiagnosticMessageSpeakerPrefixLoggingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[MessageSpeakerPrefix] Prepending speaker '{speakerName}' to message '{value}'");
                }

                _isApplying = true;
                try
                {
                    value = prefix + value;
                    int limit = KupoUIPRPlugin.DialogueLineLengthLimitConfig.Value;
                    if (limit > 0)
                    {
                        value = WrapText(value, limit);
                    }
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

    internal static string WrapText(string text, int maxCharsPerLine)
    {
        if (string.IsNullOrEmpty(text) || maxCharsPerLine <= 0)
            return text;

        // Normalize newlines to spaces, avoiding double spaces if adjacent spaces already exist
        string tempText = text.Replace("\r\n", "\n");
        StringBuilder sb = new StringBuilder();
        for (int j = 0; j < tempText.Length; j++)
        {
            if (tempText[j] == '\n')
            {
                bool hasAdjacentSpace = (j > 0 && tempText[j - 1] == ' ') || 
                                       (j < tempText.Length - 1 && tempText[j + 1] == ' ');
                if (!hasAdjacentSpace)
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(tempText[j]);
            }
        }
        string normalized = sb.ToString();

        // Check if there are spaces. If not (e.g. Japanese), wrap by character.
        bool useCharWrap = !normalized.Contains(" ");

        StringBuilder result = new StringBuilder();
        int currentLineLength = 0;
        int i = 0;

        while (i < normalized.Length)
        {
            // Parse rich-text tags without counting their length
            if (normalized[i] == '<')
            {
                int tagClose = normalized.IndexOf('>', i);
                if (tagClose != -1)
                {
                    string tag = normalized.Substring(i, tagClose - i + 1);
                    result.Append(tag);
                    i = tagClose + 1;
                    continue;
                }
            }

            if (useCharWrap)
            {
                // Character wrap: append character by character
                char c = normalized[i];
                if (currentLineLength >= maxCharsPerLine)
                {
                    result.Append('\n');
                    currentLineLength = 0;
                }
                result.Append(c);
                currentLineLength++;
                i++;
            }
            else
            {
                // Word wrap: find next space or tag
                int nextSpace = normalized.IndexOf(' ', i);
                int nextTag = normalized.IndexOf('<', i);
                int endOfToken = normalized.Length;

                if (nextSpace != -1 && nextSpace < endOfToken) endOfToken = nextSpace;
                if (nextTag != -1 && nextTag < endOfToken) endOfToken = nextTag;

                string token = normalized.Substring(i, endOfToken - i);
                int tokenLength = token.Length;

                if (currentLineLength + tokenLength > maxCharsPerLine && currentLineLength > 0)
                {
                    result.Append('\n');
                    currentLineLength = 0;
                    if (token == " ")
                    {
                        i = endOfToken;
                        continue;
                    }
                }

                result.Append(token);
                currentLineLength += tokenLength;
                i = endOfToken;

                if (i < normalized.Length && normalized[i] == ' ')
                {
                    int lookAhead = i + 1;
                    while (lookAhead < normalized.Length && normalized[lookAhead] == ' ')
                    {
                        lookAhead++;
                    }

                    int nextWordEnd = lookAhead;
                    while (nextWordEnd < normalized.Length && normalized[nextWordEnd] != ' ' && normalized[nextWordEnd] != '<')
                    {
                        nextWordEnd++;
                    }
                    int nextWordLength = nextWordEnd - lookAhead;

                    if (currentLineLength + 1 + nextWordLength > maxCharsPerLine)
                    {
                        result.Append('\n');
                        currentLineLength = 0;
                        i = lookAhead;
                    }
                    else
                    {
                        result.Append(' ');
                        currentLineLength++;
                        i++;
                    }
                }
            }
        }

        return result.ToString();
    }
}
