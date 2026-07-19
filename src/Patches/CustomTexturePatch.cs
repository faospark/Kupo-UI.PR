using System;
using HarmonyLib;
using KupoUI.PR.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class CustomTexturePatch
{
    private const string MenuPortraitPathFragment = "/chara_rect/front/front_parent/charac_parent/chara_image";

    [HarmonyPatch(typeof(Sprite), nameof(Sprite.texture), MethodType.Getter)]
    [HarmonyPostfix]
    private static void SpriteTexturePostfix(Sprite __instance, ref Texture2D __result)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures)
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
        if (!KupoUIPRPlugin.EnableCustomTextures)
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
        if (TryResolveMenuPortraitFromSpeakerPortraits(value, assetAddress, out var customSprite))
        {
            value = customSprite;
            return;
        }

        if (TextureResolver.TryCreateReplacementSprite(value, out var replacement, assetAddress))
        {
            value = replacement;
        }
    }

    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void UIImageSpritePrefix(Image __instance, ref Sprite value)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures)
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
        if (TryResolveMenuPortraitFromSpeakerPortraits(value, assetAddress, out var customSprite))
        {
            value = customSprite;
            if (IsMenuPortraitImage(__instance))
            {
                __instance.preserveAspect = true;
            }
            return;
        }

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
        if (!KupoUIPRPlugin.EnableCustomTextures)
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

    private static bool TryResolveMenuPortraitFromSpeakerPortraits(Sprite original, string assetAddress, out Sprite customSprite)
    {
        customSprite = null;
        if (!KupoUIPRPlugin.EnableCustomTextures || !KupoUIPRPlugin.EnableSpeakerPortraitsConfig.Value)
        {
            return false;
        }

        if (string.IsNullOrEmpty(assetAddress))
        {
            return false;
        }

        if (TryExtractSpeakerIdFromMenuPortraitAddress(assetAddress, out var speakerId))
        {
            string lookupId = speakerId;
            string lookupName = null;

            if (TryGetMappedSpeakerValue(assetAddress, out var mappedValue))
            {
                lookupId = mappedValue;
                if (KupoUIPRPlugin.TryGetSpeakerNameOverride(mappedValue, out var nameOverride))
                {
                    lookupName = nameOverride;
                }
                else
                {
                    lookupName = mappedValue;
                }
            }
            else
            {
                if (KupoUIPRPlugin.TryGetSpeakerNameOverride(speakerId, out var nameOverride))
                {
                    lookupName = nameOverride;
                }
                else
                {
                    var shortId = SpeakerPortraitsPatch.GetShortSpeakerId(speakerId);
                    if (shortId != speakerId && KupoUIPRPlugin.TryGetSpeakerNameOverride(shortId, out var shortNameOverride))
                    {
                        lookupName = shortNameOverride;
                    }
                }
            }

            string imagePath = SpeakerPortraitsPatch.FindPortraitFile(lookupId, lookupName);
            if (!string.IsNullOrEmpty(imagePath))
            {
                customSprite = SpeakerPortraitsPatch.GetOrCreatePortraitSprite(imagePath);
                return customSprite != null;
            }
        }

        return false;
    }

    private static bool TryGetMappedSpeakerValue(string assetAddress, out string mappedValue)
    {
        mappedValue = null;
        if (string.IsNullOrEmpty(assetAddress))
        {
            return false;
        }

        var path = assetAddress.Replace('\\', '/');

        if (KupoUIPRPlugin.MenuPortraitMap.TryGetValue(path, out mappedValue))
        {
            return true;
        }

        const string assetsPrefix = "Assets/";
        if (path.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var noAssets = path.Substring(assetsPrefix.Length);
            if (KupoUIPRPlugin.MenuPortraitMap.TryGetValue(noAssets, out mappedValue))
            {
                return true;
            }
        }

        if (TryExtractSpeakerIdFromMenuPortraitAddress(path, out var speakerId))
        {
            if (KupoUIPRPlugin.MenuPortraitMap.TryGetValue(speakerId, out mappedValue))
            {
                return true;
            }

            var shortId = SpeakerPortraitsPatch.GetShortSpeakerId(speakerId);
            if (shortId != speakerId && KupoUIPRPlugin.MenuPortraitMap.TryGetValue(shortId, out mappedValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractSpeakerIdFromMenuPortraitAddress(string assetAddress, out string speakerId)
    {
        speakerId = null;
        if (string.IsNullOrEmpty(assetAddress))
        {
            return false;
        }

        var path = assetAddress.Replace('\\', '/');

        const string faceMarker = "Chara/Face/";
        var markerIdx = path.IndexOf(faceMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0)
        {
            return false;
        }

        var relative = path.Substring(markerIdx + faceMarker.Length);
        var parts = relative.Split('/');
        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
        {
            speakerId = parts[0];
            return true;
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
            path = current.name + "/" + path;
            current = current.parent;
        }

        return !string.IsNullOrEmpty(path);
    }
}
