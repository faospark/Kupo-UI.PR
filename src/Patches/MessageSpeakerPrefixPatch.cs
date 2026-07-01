using System;
using HarmonyLib;
using Last.Message;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Prepends the speaker name to every dialogue message so that it reads
/// "Maria: I have been worried" instead of just "I have been worried".
///
/// STRATEGY:
/// Harmony cannot patch the IL2CPP interop wrapper's SetMessage directly
/// because the game invokes the underlying native method, not the managed
/// shim. Instead we intercept <see cref="Text.text"/>'s setter and check
/// whether the Text being written to is the <c>messageText</c> field of a
/// <see cref="MessageWindowView"/> living on the same GameObject hierarchy.
///
/// OVERFLOW FIX:
/// A configurable font size is applied to both the speaker Text and the
/// message Text whenever a speaker is present, keeping everything at a
/// consistent, readable size that fits the existing UI box.
/// </summary>
[HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
internal static class MessageSpeakerPrefixPatch
{
    private const string Separator = ": ";

    /// <summary>
    /// Reentrancy guard: set to <c>true</c> while we are rewriting <c>value</c>
    /// so that the setter we trigger on ourselves does not loop.
    /// </summary>
#pragma warning disable CS0649 // assigned via direct field write inside prefix
    [ThreadStatic]
    private static bool _isApplying;
#pragma warning restore CS0649

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
            KupoUIPRPlugin.PluginLog.LogDebug("[MessageSpeakerPrefix] Skipped – empty value.");
            return;
        }

        // Walk up the hierarchy looking for a MessageWindowView.
        var view = FindMessageWindowView(__instance);
        if (view == null)
        {
            return; // Not a message text we care about.
        }

        KupoUIPRPlugin.PluginLog.LogDebug(
            $"[MessageSpeakerPrefix] Caught Text.set_text on '{__instance.name}' " +
            $"under '{__instance.gameObject?.name}', value='{value}'");

        // Confirm this Text IS the messageText field (not spekerText itself).
        var msgText = view.messageText;
        if (msgText == null || msgText.Pointer != __instance.Pointer)
        {
            KupoUIPRPlugin.PluginLog.LogDebug(
                "[MessageSpeakerPrefix] Skipped – Text is not the messageText field of the view.");
            return;
        }

        // Get the speaker name.
        var spekerText = view.spekerText;
        var speakerName = spekerText != null ? spekerText.text : null;

        KupoUIPRPlugin.PluginLog.LogDebug(
            $"[MessageSpeakerPrefix] speakerName='{speakerName ?? "(null)"}'");

        if (string.IsNullOrWhiteSpace(speakerName))
        {
            return;
        }

        var prefix = speakerName + Separator;

        // Guard against double-prefix if this fires twice.
        if (value.StartsWith(prefix, StringComparison.Ordinal))
        {
            KupoUIPRPlugin.PluginLog.LogDebug("[MessageSpeakerPrefix] Already prefixed, skipping.");
            return;
        }

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[MessageSpeakerPrefix] Prepending speaker '{speakerName}' to message '{value}'");

        value = prefix + value;

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
    /// Returns <c>null</c> if none found within a reasonable depth.
    /// </summary>
    private static MessageWindowView FindMessageWindowView(Text text)
    {
        if (text == null || text.gameObject == null)
        {
            return null;
        }

        // Check own GameObject first, then parents (the Text is usually a
        // child of the MessageWindowView's GameObject).
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
