using HarmonyLib;
using KupoUI.PR.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Creates a full-screen background image (<c>fullbg</c>) as a sibling of the title-screen
/// <c>background</c> object, rendered on top of it, when a texture named
/// <c>TitlescreenFullBG</c> is present in any mod folder under <c>Modules/00-Mods/</c>.
/// <para>
/// If no matching texture file is found the object is never created and nothing changes.
/// </para>
/// </summary>
[HarmonyPatch]
internal static class TitleScreenFullBgPatch
{
    /// <summary>Texture name looked up in the mod folder index.</summary>
    private const string TextureName = "TitlescreenFullBG";

    /// <summary>Name given to the injected GameObject.</summary>
    private const string FullBgObjectName = "fullbg";

    /// <summary>
    /// Path fragment used to confirm a <c>Graphic</c> is the title-screen background.
    /// Must appear anywhere in the built transform path.
    /// </summary>
    private const string TargetPathFragment =
        "/background_canvas/ui_root/backgrou_root/background";

    // ── Graphic.OnEnable ─────────────────────────────────────────────────────
    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    [HarmonyPostfix]
    private static void GraphicOnEnablePostfix(Graphic __instance)
    {
        if (!IsTargetBackground(__instance))
        {
            return;
        }

        TryCreateFullBg(__instance.transform.parent);
    }

    // ── Graphic.UpdateMaterial ────────────────────────────────────────────────
    // FFPR re-serializes UI values here; ensure we catch late activations too.
    [HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
    [HarmonyPostfix]
    private static void GraphicUpdateMaterialPostfix(Graphic __instance)
    {
        if (!IsTargetBackground(__instance))
        {
            return;
        }

        TryCreateFullBg(__instance.transform.parent);
    }

    // ── Core creation logic ───────────────────────────────────────────────────

    private static void TryCreateFullBg(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        // Already injected — nothing to do.
        if (parent.Find(FullBgObjectName) != null)
        {
            return;
        }

        // Bail out early (no log spam) when no texture is registered.
        if (!TextureResolver.HasTextureOverride(TextureName))
        {
            return;
        }

        // Load the texture into a fresh Texture2D via TextureResolver's in-place loader.
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.name = TextureName;

        if (!TextureResolver.TryReplaceTextureInPlace(tex, TextureName))
        {
            KupoUIPRPlugin.PluginLog.LogWarning(
                $"[TitleScreenFullBg] Texture '{TextureName}' is indexed but could not be loaded.");
            return;
        }

        // Keep the texture alive across scene reloads.
        UnityEngine.Object.DontDestroyOnLoad(tex);

        // ── Create the GameObject ────────────────────────────────────────────
        var go = new GameObject(FullBgObjectName);
        go.transform.SetParent(parent, false);

        // Place immediately above 'background' in the sibling order so it
        // renders on top of the solid-color background but below any UI elements.
        var bgTransform = parent.Find("background");
        if (bgTransform != null)
        {
            go.transform.SetSiblingIndex(bgTransform.GetSiblingIndex() + 1);
        }

        // ── RectTransform: stretch to fill the parent ────────────────────────
        var rt = go.GetComponent<RectTransform>();
        if (rt == null)
        {
            rt = go.AddComponent<RectTransform>();
        }

        rt.anchorMin = Vector2.zero;   // anchor bottom-left
        rt.anchorMax = Vector2.one;    // anchor top-right (full stretch)
        rt.offsetMin = Vector2.zero;   // no margin
        rt.offsetMax = Vector2.zero;

        // ── RawImage: display the custom texture ─────────────────────────────
        var rawImage = go.AddComponent<RawImage>();
        rawImage.texture = tex;
        rawImage.color   = Color.white;

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[TitleScreenFullBg] Injected '{FullBgObjectName}' ({tex.width}x{tex.height}) " +
            $"above '{bgTransform?.name ?? "background"}' under '{parent.name}'.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsTargetBackground(Graphic graphic)
    {
        if (graphic == null || graphic.transform == null)
        {
            return false;
        }

        if (!graphic.name.Equals("background", System.StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryBuildTransformPath(graphic.transform, out var path))
        {
            return false;
        }

        return path.IndexOf(TargetPathFragment, System.StringComparison.OrdinalIgnoreCase) >= 0;
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
}
