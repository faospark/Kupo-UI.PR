using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class TitleScreenBgColorPatch
{
    // Path fragment that must appear anywhere in the built transform path.
    // Note: "backgrou_root" matches the in-game hierarchy name as-is.
    private const string TargetPathFragment =
        "/background_canvas/ui_root/backgrou_root/background";

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

        var resolved = ResolveColor(KupoUIPRPlugin.TitleScreenBgColorConfig.Value);
        if (!resolved.HasValue)
        {
            return; // "original" — leave the game's value untouched.
        }

        value = resolved.Value;
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

        ApplyColor(__instance);
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

        ApplyColor(__instance);
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the configured background color to <paramref name="graphic"/>.
    /// Does nothing when the config is set to <c>original</c>.
    /// </summary>
    private static void ApplyColor(Graphic graphic)
    {
        var resolved = ResolveColor(KupoUIPRPlugin.TitleScreenBgColorConfig.Value);
        if (!resolved.HasValue)
        {
            return; // "original" — do nothing.
        }

        var targetColor = resolved.Value;
        if (!AreApproximatelyEqual(graphic.color, targetColor))
        {
            _isApplying = true;
            try
            {
                graphic.color = targetColor;
            }
            finally
            {
                _isApplying = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>null</c> for <c>original</c> (meaning: don't override),
    /// or the resolved <see cref="Color"/> for all other options.
    /// </summary>
    private static Color? ResolveColor(string colorName) =>
        (colorName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "original" => null,
            "white"    => new Color(1f,     1f,     1f,     1f),
            "navy"     => new Color(0f,     0f,     0.502f, 1f),
            "crimson"  => new Color(0.863f, 0.078f, 0.235f, 1f),
            "violet"   => new Color(0.502f, 0f,     0.502f, 1f),
            _          => new Color(0f,     0f,     0f,     1f), // black (default)
        };

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="graphic"/> is the
    /// <c>background</c> object in the title-screen background hierarchy.
    /// </summary>
    private static bool IsTargetGraphic(Graphic graphic)
    {
        if (graphic == null || graphic.transform == null)
        {
            return false;
        }

        if (!graphic.name.Equals("background", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryBuildTransformPath(graphic.transform, out var path))
        {
            return false;
        }

        return path.IndexOf(TargetPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
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
