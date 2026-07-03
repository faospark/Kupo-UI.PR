using System;
using HarmonyLib;
using Last.Message;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Independently forces a fixed font size (and disables best-fit) on dialogue and
/// speaker <see cref="Text"/> components, controlled solely by
/// <see cref="KupoUIPRPlugin.DialogueFontSizeConfig"/>.
/// Works regardless of whether <see cref="KupoUIPRPlugin.MessageSpeakerPrefixConfig"/> is enabled.
/// </summary>
[HarmonyPatch]
internal static class DialogueFontSizePatch
{
    // ── FORCE BEST FIT TO REMAIN FALSE FOR DIALOGUE TEXT ─────────────────
    [HarmonyPatch(typeof(Text), nameof(Text.resizeTextForBestFit), MethodType.Setter)]
    [HarmonyPrefix]
    private static void ResizeTextForBestFitSetterPrefix(Text __instance, ref bool value)
    {
        if (IsDialogueText(__instance, out _))
        {
            value = false; // Force it to remain false
        }
    }

    // ── FORCE FONT SIZE ON DIALOGUE AND SPEAKER TEXTS ─────────────────────
    [HarmonyPatch(typeof(Text), nameof(Text.fontSize), MethodType.Setter)]
    [HarmonyPrefix]
    private static void FontSizeSetterPrefix(Text __instance, ref int value)
    {
        if (IsDialogueText(__instance, out var targetSize))
        {
            value = targetSize; // Force it to our target size
        }
    }

    // ── ENFORCE FONT SIZE WHEN DIALOGUE TEXT CHANGES ──────────────────────
    [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
    [HarmonyPostfix]
    private static void TextSetterPostfix(Text __instance)
    {
        if (IsDialogueText(__instance, out var targetSize))
        {
            ApplyFontSize(__instance, targetSize);
        }
    }

    // ── OVERRIDE SERIALIZATION RESETS VIA GRAPHIC HOOKS ────────────────────
    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    [HarmonyPostfix]
    private static void GraphicOnEnablePostfix(Graphic __instance)
    {
        if (__instance is Text textComp && IsDialogueText(textComp, out var targetSize))
        {
            if (textComp.fontSize != targetSize)
                textComp.fontSize = targetSize;
            if (textComp.resizeTextForBestFit)
                textComp.resizeTextForBestFit = false;
        }
    }

    [HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
    [HarmonyPostfix]
    private static void GraphicUpdateMaterialPostfix(Graphic __instance)
    {
        if (__instance is Text textComp && IsDialogueText(textComp, out var targetSize))
        {
            if (textComp.fontSize != targetSize)
                textComp.fontSize = targetSize;
            if (textComp.resizeTextForBestFit)
                textComp.resizeTextForBestFit = false;
        }
    }

    // ── HELPERS ───────────────────────────────────────────────────────────

    internal static bool TryGetTargetSize(out int targetSize)
    {
        targetSize = 0;
        var raw = KupoUIPRPlugin.DialogueFontSizeConfig.Value;
        return !string.IsNullOrWhiteSpace(raw)
            && !raw.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw.Trim(), out targetSize)
            && targetSize > 0;
    }

    private static bool IsDialogueText(Text textComp, out int targetSize)
    {
        targetSize = 0;
        if (textComp == null) return false;

        if (!TryGetTargetSize(out targetSize)) return false;

        var view = FindMessageWindowView(textComp);
        if (view == null) return false;

        return (view.messageText != null && view.messageText.Pointer == textComp.Pointer)
            || (view.spekerText != null && view.spekerText.Pointer == textComp.Pointer);
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

    internal static void ApplyFontSize(Text text, int size)
    {
        if (text == null) return;

        if (text.fontSize != size)
        {
            text.fontSize = size;
            KupoUIPRPlugin.PluginLog.LogDebug(
                $"[DialogueFontSize] fontSize set to {size} on '{text.name}'.");
        }
    }
}
