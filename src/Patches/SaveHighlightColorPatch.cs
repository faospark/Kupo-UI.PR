using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DarkerUI.PR.Patches;

[HarmonyPatch]
internal static class SaveHighlightColorPatch
{
    private const float SaveHighlightAlpha = 0.698f;
    private const string SaveHighlightPathFragment = "/ui_root/loadGame(Clone)/save_info/menu/content_list/Scroll View/Viewport/Content/save_slot(Clone)/image_blue";

    private static readonly Color DarkNavyColor = new(0.0500f, 0.1000f, 0.2500f, SaveHighlightAlpha);
    private static readonly Color DarkGreenColor = new(0.0500f, 0.3000f, 0.1400f, SaveHighlightAlpha);
    private static readonly Color DarkVioletColor = new(0.2600f, 0.1200f, 0.4200f, SaveHighlightAlpha);
    private static readonly Color DarkYellowColor = new(0.5500f, 0.4500f, 0.0500f, SaveHighlightAlpha);
    private static readonly Color DarkOrangeColor = new(0.5500f, 0.2500f, 0.0500f, SaveHighlightAlpha);

    private static bool _isApplying;

    [HarmonyPatch(typeof(Graphic), nameof(Graphic.color), MethodType.Setter)]
    [HarmonyPrefix]
    private static void GraphicColorSetterPrefix(Graphic __instance, ref Color value)
    {
        if (!IsTargetSaveHighlight(__instance))
        {
            return;
        }

        if (ShouldDisableTarget())
        {
            if (__instance.enabled)
            {
                __instance.enabled = false;
            }

            return;
        }

        if (!TryGetConfiguredColor(out var configuredColor))
        {
            return;
        }

        value = configuredColor;
    }

    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    [HarmonyPostfix]
    private static void GraphicOnEnablePostfix(Graphic __instance)
    {
        if (!IsTargetSaveHighlight(__instance))
        {
            return;
        }

        if (ShouldDisableTarget())
        {
            if (__instance.enabled)
            {
                __instance.enabled = false;
            }

            return;
        }

        if (!__instance.enabled)
        {
            __instance.enabled = true;
        }

        if (!TryGetConfiguredColor(out var configuredColor))
        {
            return;
        }

        if (_isApplying)
        {
            return;
        }

        if (AreApproximatelyEqual(__instance.color, configuredColor))
        {
            return;
        }

        _isApplying = true;
        try
        {
            __instance.color = configuredColor;
        }
        finally
        {
            _isApplying = false;
        }
    }

    [HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
    [HarmonyPostfix]
    private static void GraphicUpdateMaterialPostfix(Graphic __instance)
    {
        // Some FFPR UI flows re-apply serialized values after enable; enforce once more here.
        if (!IsTargetSaveHighlight(__instance))
        {
            return;
        }

        if (ShouldDisableTarget())
        {
            if (__instance.enabled)
            {
                __instance.enabled = false;
            }

            return;
        }

        if (!__instance.enabled)
        {
            __instance.enabled = true;
        }

        if (!TryGetConfiguredColor(out var configuredColor))
        {
            return;
        }

        if (_isApplying || AreApproximatelyEqual(__instance.color, configuredColor))
        {
            return;
        }

        _isApplying = true;
        try
        {
            __instance.color = configuredColor;
        }
        finally
        {
            _isApplying = false;
        }
    }

    private static bool ShouldDisableTarget()
    {
        var configured = DarkerUIPRPlugin.SaveHighlightColorConfig.Value ?? string.Empty;
        var normalized = configured.Trim();
        return normalized.Equals("Disable", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Off", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetConfiguredColor(out Color color)
    {
        color = default;

        var configured = DarkerUIPRPlugin.SaveHighlightColorConfig.Value ?? string.Empty;
        var normalized = configured.Trim();

        if (normalized.Length == 0 || normalized.Equals("Original", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ShouldDisableTarget())
        {
            return false;
        }

        if (normalized.Equals("DarkNavy", StringComparison.OrdinalIgnoreCase))
        {
            color = DarkNavyColor;
            return true;
        }

        if (normalized.Equals("DarkGreen", StringComparison.OrdinalIgnoreCase))
        {
            color = DarkGreenColor;
            return true;
        }

        if (normalized.Equals("DarkViolet", StringComparison.OrdinalIgnoreCase))
        {
            color = DarkVioletColor;
            return true;
        }

        if (normalized.Equals("DarkYellow", StringComparison.OrdinalIgnoreCase))
        {
            color = DarkYellowColor;
            return true;
        }

        if (normalized.Equals("DarkOrange", StringComparison.OrdinalIgnoreCase))
        {
            color = DarkOrangeColor;
            return true;
        }

        // Unknown value: fail safe to DarkNavy so UI remains deterministic.
        color = DarkNavyColor;
        return true;
    }

    private static bool IsTargetSaveHighlight(Graphic graphic)
    {
        if (graphic == null || graphic.transform == null)
        {
            return false;
        }

        if (!graphic.name.Equals("image_blue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryBuildTransformPath(graphic.transform, out var path)
            && path.IndexOf(SaveHighlightPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryBuildTransformPath(Transform transform, out string path)
    {
        path = null;
        if (transform == null)
        {
            return false;
        }

        path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return !string.IsNullOrEmpty(path);
    }

    private static bool AreApproximatelyEqual(Color left, Color right)
    {
        const float epsilon = 0.0005f;
        return Math.Abs(left.r - right.r) < epsilon
            && Math.Abs(left.g - right.g) < epsilon
            && Math.Abs(left.b - right.b) < epsilon
            && Math.Abs(left.a - right.a) < epsilon;
    }
}
