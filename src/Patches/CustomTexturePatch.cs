using System;
using HarmonyLib;
using DarkerUI.PR.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace DarkerUI.PR.Patches;

[HarmonyPatch]
internal static class CustomTexturePatch
{
    private const string MenuPortraitPathFragment = "/chara_rect/front/front_parent/charac_parent/chara_image";

    [HarmonyPatch(typeof(Sprite), nameof(Sprite.texture), MethodType.Getter)]
    [HarmonyPostfix]
    private static void SpriteTexturePostfix(Sprite __instance, ref Texture2D __result)
    {
        if (!DarkerUIPRPlugin.EnableCustomTexturesConfig.Value)
        {
            return;
        }

        if (__result == null)
        {
            return;
        }

        if (__result.name.EndsWith("_Custom"))
        {
            return;
        }

        // Resolve the addressable asset address for the sprite so that the
        // path-based index can be used when the same filename appears in multiple bundles.
        AssetAddressTracker.TryGetAddress(__instance, __result, out var assetAddress);

        if (TextureResolver.IsLikelyAtlasTextureName(__result.name)
            && !TextureResolver.HasTextureOverride(__result.name)
            && !TextureResolver.HasPathOverride(assetAddress))
        {
            // Atlas textures are only replaced when an explicit atlas override file exists.
            return;
        }

        TextureLogger.LogObservedTextureName(__result.name, "Sprite.texture.get");

        TextureResolver.TryReplaceTextureInPlace(__result, __result.name, assetAddress);
    }

    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void SpriteRendererSpritePrefix(ref Sprite value)
    {
        if (!DarkerUIPRPlugin.EnableCustomTexturesConfig.Value)
        {
            return;
        }

        if (value == null)
        {
            return;
        }

        if (value.name.EndsWith("_Custom"))
        {
            return;
        }

        TextureLogger.LogObservedTextureName(value.name, "SpriteRenderer.sprite.set:sprite");
        if (value.texture != null)
        {
            TextureLogger.LogObservedTextureName(value.texture.name, "SpriteRenderer.sprite.set:texture");
        }

        AssetAddressTracker.TryGetAddress(value, value.texture, out var assetAddress);
        if (TextureResolver.TryCreateReplacementSprite(value, out var replacement, assetAddress))
        {
            value = replacement;
        }
    }

    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void UIImageSpritePrefix(Image __instance, ref Sprite value)
    {
        if (!DarkerUIPRPlugin.EnableCustomTexturesConfig.Value)
        {
            return;
        }

        if (value == null)
        {
            return;
        }

        if (value.name.EndsWith("_Custom"))
        {
            return;
        }

        TextureLogger.LogObservedTextureName(value.name, "UI.Image.sprite.set:sprite");
        if (value.texture != null)
        {
            TextureLogger.LogObservedTextureName(value.texture.name, "UI.Image.sprite.set:texture");
        }

        AssetAddressTracker.TryGetAddress(value, value.texture, out var assetAddress);
        if (TextureResolver.TryCreateReplacementSprite(value, out var replacement, assetAddress))
        {
            value = replacement;

            if (IsMenuPortraitImage(__instance))
            {
                __instance.preserveAspect = true;
            }
        }
    }

    [HarmonyPatch(typeof(Image), nameof(Image.SetNativeSize))]
    [HarmonyPrefix]
    private static bool UIImageSetNativeSizePrefix(Image __instance)
    {
        if (!DarkerUIPRPlugin.EnableCustomTexturesConfig.Value)
        {
            return true;
        }

        if (!IsMenuPortraitImage(__instance))
        {
            return true;
        }

        var sprite = __instance.sprite;
        if (sprite == null || !sprite.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsMenuPortraitImage(Image image)
    {
        if (image == null)
        {
            return false;
        }

        return TryBuildTransformPath(image.transform, out var path)
            && path.IndexOf(MenuPortraitPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
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
}
