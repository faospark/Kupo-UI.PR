using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class TitleScreenTextPatch
{
    // Fragments that must appear anywhere in the built transform path.
    private static readonly string[] TargetPathFragments =
    {
        "/menu_canvas/ui_root/notch_root/title(Clone)/content_root/menu/main_menu/title_command_content(Clone)/last_text",
        "/menu_canvas/ui_root/notch_root/title(Clone)/content_root/menu/sub_menu/title_command_content(Clone)/last_text",
        "/menu_canvas/ui_root/notch_root/title(Clone)/content_root/menu/option_menu/title_command_content(Clone)/last_text",
    };

    private static bool _isApplying;

    // ── Graphic.color setter ──────────────────────────────────────────────
    // Intercept any color write on the target and replace it with our value.
    [HarmonyPatch(typeof(Graphic), nameof(Graphic.color), MethodType.Setter)]
    [HarmonyPrefix]
    private static void GraphicColorSetterPrefix(Graphic __instance, ref Color value)
    {
        if (_isApplying)
        {
            return;
        }

        if (!IsTargetGraphic(__instance))
        {
            return;
        }

        if (!KupoUIPRPlugin.TitleScreenTextWhiteConfig.Value)
        {
            return;
        }

        value = Color.white;

        TryApplyFontSize(__instance);
    }

    // ── Graphic.OnEnable ──────────────────────────────────────────────────
    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    [HarmonyPostfix]
    private static void GraphicOnEnablePostfix(Graphic __instance)
    {
        if (_isApplying)
        {
            return;
        }

        if (!IsTargetGraphic(__instance))
        {
            return;
        }

        ApplyAll(__instance);
    }

    // ── Graphic.UpdateMaterial ────────────────────────────────────────────
    // FFPR re-serializes values after enable; enforce once more here.
    [HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
    [HarmonyPostfix]
    private static void GraphicUpdateMaterialPostfix(Graphic __instance)
    {
        if (_isApplying)
        {
            return;
        }

        if (!IsTargetGraphic(__instance))
        {
            return;
        }

        ApplyAll(__instance);
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply color directly through the <see cref="Graphic"/> base reference
    /// (works regardless of whether the IL2CPP Text proxy is resolvable).
    /// </summary>
    private static void ApplyAll(Graphic graphic)
    {
        if (!KupoUIPRPlugin.TitleScreenTextWhiteConfig.Value)
        {
            return;
        }

        if (!AreApproximatelyEqual(graphic.color, Color.white))
        {
            _isApplying = true;
            try
            {
                graphic.color = Color.white;
            }
            finally
            {
                _isApplying = false;
            }
        }

        TryApplyFontSize(graphic);
        TryDisableShadow(graphic);
    }

    /// <summary>
    /// Attempt to resolve a <see cref="Text"/> component from the same
    /// GameObject and apply the configured font size.
    /// </summary>
    private static void TryApplyFontSize(Graphic graphic)
    {
        if (graphic.gameObject == null)
        {
            return;
        }

        var text = graphic.gameObject.GetComponent<Text>();
        if (text == null)
        {
            return;
        }

        var targetSize = KupoUIPRPlugin.TitleScreenTextFontSizeConfig.Value;
        if (text.fontSize != targetSize)
        {
            text.fontSize = targetSize;
        }
    }

    /// <summary>
    /// Disables all <see cref="Shadow"/> components on the same GameObject
    /// when <see cref="KupoUIPRPlugin.TitleScreenTextDisableShadowConfig"/> is enabled.
    /// </summary>
    private static void TryDisableShadow(Graphic graphic)
    {
        if (!KupoUIPRPlugin.TitleScreenTextDisableShadowConfig.Value)
        {
            return;
        }

        if (graphic.gameObject == null)
        {
            return;
        }

        foreach (var shadow in graphic.gameObject.GetComponents<Shadow>())
        {
            if (shadow != null && shadow.enabled)
            {
                shadow.enabled = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="graphic"/> is the
    /// <c>last_text</c> object in the title-screen menu hierarchy.
    /// Does NOT require a successful Text component cast.
    /// </summary>
    private static bool IsTargetGraphic(Graphic graphic)
    {
        if (graphic == null || graphic.transform == null)
        {
            return false;
        }

        if (!graphic.name.Equals("last_text", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryBuildTransformPath(graphic.transform, out var path))
        {
            return false;
        }

        foreach (var fragment in TargetPathFragments)
        {
            if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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
            path    = current.name + "/" + path;
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
