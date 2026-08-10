using System;
using System.Collections.Generic;
using HarmonyLib;
using KupoUI.PR.Textures;
using Last.UI.Touch;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class CustomTexturePatch
{
    private const string MenuPortraitPathFragment = "/chara_rect/front/front_parent/charac_parent/chara_image";

    // [OPT-PERF] Tracks instance IDs of sprites whose Sprite.texture getter has already been
    // handled. The getter fires on every frame for every visible UI element during scrolling.
    // This set prevents repeated string allocations and dictionary probes for already-processed sprites.
    private static readonly HashSet<int> SpriteTextureProcessedIds = new();

    internal static void ClearCache()
    {
        SpriteTextureProcessedIds.Clear();
    }

    internal static void RemoveFromCaches(int spriteInstanceId)
    {
        SpriteTextureProcessedIds.Remove(spriteInstanceId);
    }

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

        // [OPT-PERF] Skip sprites we have already handled — avoids per-frame work during scrolling.
        var spriteId = __instance.GetInstanceID();
        if (SpriteTextureProcessedIds.Contains(spriteId))
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
            SpriteTextureProcessedIds.Add(spriteId);
            return;
        }

        TextureLogger.LogObservedTextureName(__result.name, "Sprite.texture.get");

        TextureResolver.TryReplaceTextureInPlace(__result, __result.name, assetAddress);

        // Mark as processed regardless of success so we don't retry on every frame.
        SpriteTextureProcessedIds.Add(spriteId);
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
        if (!KupoUIPRPlugin.EnableCustomTextures) return;
        if (value == null) return;
        if (value.name.EndsWith("_Custom")) return;

        AssetAddressTracker.TryGetAddress(value, value.texture, out var assetAddress);

        // Add explicit trace for Bestiary image component
        if (__instance.name == "Image" && KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[CustomTexturePatch] Image.sprite setter prefix: name={__instance.name}, spriteName={value.name}, textureName={value.texture?.name}, address={assetAddress}");
        }

        if (TryResolveMenuPortraitFromSpeakerPortraits(value, assetAddress, out var customSprite))
        {
            value = customSprite;
            __instance.preserveAspect = true;
            return;
        }

        if (TextureResolver.TryCreateReplacementSprite(value, out var replacement, assetAddress, isUi: true))
        {
            value = replacement;
            if (__instance.name == "Image" && KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[CustomTexturePatch]   Sprite replaced with custom sprite: {replacement.name}");
            }
        }

        // Apply metadata sizing/offsets regardless of whether TryCreateReplacementSprite returned true or false.
        // As long as the sprite has custom metadata (defined by sprite name or texture name), we resize the UI transform.
        var metadata = GetMetadataForSprite(value, assetAddress);
        if (metadata != null)
        {
            if (__instance.name == "Image")
            {
                var rt = __instance.rectTransform;
                var parentRt = __instance.transform.parent != null ? __instance.transform.parent.GetComponent<UnityEngine.RectTransform>() : null;

                KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] Metadata found! targetSize={metadata.Width}x{metadata.Height}, offset=({metadata.OffsetX}, {metadata.OffsetY})");
                KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   BEFORE: Image sizeDelta={rt?.sizeDelta}, localScale={__instance.transform.localScale}, anchoredPosition={rt?.anchoredPosition}");
                KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   BEFORE: Parent sizeDelta={parentRt?.sizeDelta}, localScale={parentRt?.transform.localScale}, anchoredPosition={parentRt?.anchoredPosition}");

                // Print components on this GameObject using ToString() for actual native types
                var comps = __instance.GetComponents<UnityEngine.Component>();
                foreach (var c in comps)
                {
                    if (c != null)
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   Component on Image: {c.ToString()} (Type: {c.GetType().FullName})");
                    }
                }

                // Print components on parent GameObject
                if (__instance.transform.parent != null)
                {
                    var parentComps = __instance.transform.parent.GetComponents<UnityEngine.Component>();
                    foreach (var pc in parentComps)
                    {
                        if (pc != null)
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   Component on Parent ({__instance.transform.parent.name}): {pc.ToString()} (Type: {pc.GetType().FullName})");
                        }
                    }
                }
            }

            if (IsMenuPortraitImage(__instance))
            {
                __instance.preserveAspect = true;
            }
            else
            {
                // For the Bestiary/Library monster image, preserve aspect ratio and use Simple type
                if (__instance.name == "Image")
                {
                    __instance.preserveAspect = true;
                    __instance.type = UnityEngine.UI.Image.Type.Simple;
                }

                // Disable AspectRatioFitter if it is active on this Image GameObject
                var fitter = __instance.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                if (fitter != null && fitter.enabled)
                {
                    fitter.enabled = false;
                    KupoUIPRPlugin.PluginLog.LogWarning("[BestiaryDiag]   Disabled AspectRatioFitter on Image component.");
                }

                // Disable LayoutElement if it is active
                var layoutElement = __instance.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement != null && layoutElement.enabled)
                {
                    layoutElement.enabled = false;
                    KupoUIPRPlugin.PluginLog.LogWarning("[BestiaryDiag]   Disabled LayoutElement on Image component.");
                }

                // Force localScale to (1, 1, 1) to bypass any game layout squishing
                __instance.transform.localScale = UnityEngine.Vector3.one;

                int targetW = metadata.Width > 0 ? metadata.Width : (int)value.rect.width;
                int targetH = metadata.Height > 0 ? metadata.Height : (int)value.rect.height;

                if (metadata.Width > 0 || metadata.Height > 0)
                {
                    if (__instance.rectTransform != null)
                    {
                        __instance.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
                        __instance.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);
                        if (__instance.name == "Image")
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   Applied sizes to rectTransform: {targetW}x{targetH}");
                        }
                    }
                }

                if (metadata.OffsetX.HasValue || metadata.OffsetY.HasValue)
                {
                    if (__instance.rectTransform != null)
                    {
                        float ox = metadata.OffsetX ?? 0f;
                        float oy = metadata.OffsetY ?? 0f;
                        __instance.rectTransform.anchoredPosition = new Vector2(ox, oy);
                        if (__instance.name == "Image")
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   Applied anchoredPosition to rectTransform: ({ox}, {oy})");
                        }
                    }
                }

                if (__instance.name == "Image")
                {
                    var rt = __instance.rectTransform;
                    var parentRt = __instance.transform.parent != null ? __instance.transform.parent.GetComponent<UnityEngine.RectTransform>() : null;
                    KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   AFTER: Image sizeDelta={rt?.sizeDelta}, localScale={__instance.transform.localScale}, anchoredPosition={rt?.anchoredPosition}");
                    KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   AFTER: Parent sizeDelta={parentRt?.sizeDelta}, localScale={parentRt?.transform.localScale}, anchoredPosition={parentRt?.anchoredPosition}");

                    // Print full hierarchy path and properties of all parents up to RootObject
                    var curr = __instance.transform;
                    while (curr != null)
                    {
                        var currRt = curr.GetComponent<UnityEngine.RectTransform>();
                        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag]   Hie: {curr.name} | sizeDelta={currRt?.sizeDelta} | scale={curr.localScale} | pos={currRt?.anchoredPosition} | active={curr.gameObject.activeSelf}");
                        curr = curr.parent;
                    }
                }

            }
        }
    }

    internal static TextureResolver.TextureOverrideMetadata GetMetadataForSprite(Sprite sprite, string assetAddress)
    {
        if (sprite == null) return null;
        var spriteName = sprite.name;
        if (string.IsNullOrEmpty(spriteName) && sprite.texture != null)
        {
            spriteName = sprite.texture.name;
        }

        if (!string.IsNullOrEmpty(spriteName) && spriteName.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            spriteName = spriteName.Substring(0, spriteName.Length - 7);
        }

        if (string.IsNullOrEmpty(spriteName)) return null;

        var normalizedName = TextureResolver.NormalizeName(spriteName);
        string filePath = null;

        if (!string.IsNullOrEmpty(assetAddress) && TextureResolver.TryGetFilePathByAddress(assetAddress, out var addressedPath))
        {
            filePath = addressedPath;
        }
        else if (TextureResolver.TryGetFilePathByNormalizedName(normalizedName, out var normalPath))
        {
            filePath = normalPath;
        }

        if (filePath != null)
        {
            return TextureResolver.LoadTextureMetadata(filePath);
        }
        return null;
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

        if (image.name.Equals("chara_image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryBuildTransformPath(image.transform, out var path)
            && (path.IndexOf(MenuPortraitPathFragment, StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("chara_image", StringComparison.OrdinalIgnoreCase) >= 0);
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

        if (KupoUIPRPlugin.TryGetMenuPortraitMap(path, out mappedValue))
        {
            return true;
        }

        const string assetsPrefix = "Assets/";
        if (path.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var noAssets = path.Substring(assetsPrefix.Length);
            if (KupoUIPRPlugin.TryGetMenuPortraitMap(noAssets, out mappedValue))
            {
                return true;
            }
        }

        if (TryExtractSpeakerIdFromMenuPortraitAddress(path, out var speakerId))
        {
            if (KupoUIPRPlugin.TryGetMenuPortraitMap(speakerId, out mappedValue))
            {
                return true;
            }

            var shortId = SpeakerPortraitsPatch.GetShortSpeakerId(speakerId);
            if (shortId != speakerId && KupoUIPRPlugin.TryGetMenuPortraitMap(shortId, out mappedValue))
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

    [HarmonyPatch(typeof(LibraryInfoController), nameof(LibraryInfoController.SetImage))]
    [HarmonyPostfix]
    private static void SetImagePostfix(LibraryInfoController __instance, Sprite imageSprite)
    {
        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] SetImagePostfix called! sprite={imageSprite?.name}");
        if (imageSprite == null) return;
        ApplyCustomSizingToMenuImage(__instance, imageSprite);
    }

    [HarmonyPatch(typeof(LibraryInfoController), nameof(LibraryInfoController.UpdateView))]
    [HarmonyPostfix]
    private static void UpdateViewPostfix(LibraryInfoController __instance)
    {
        var image = (__instance.view != null) ? __instance.view.monsterImage : null;
        var sprite = (image != null) ? image.sprite : null;
        
        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] UpdateViewPostfix called! view={__instance.view != null}, image={image != null}, sprite={sprite?.name}");

        if (sprite != null)
        {
            ApplyCustomSizingToMenuImage(__instance, sprite);
        }
    }

    private static void ApplyCustomSizingToMenuImage(LibraryInfoController controller, Sprite sprite)
    {
        if (controller == null || sprite == null) return;

        // Retrieve metadata for this sprite name
        var metadata = GetMetadataForSprite(sprite, null);
        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] ApplyCustomSizingToMenuImage: sprite={sprite.name}, hasMetadata={metadata != null}");

        if (metadata != null)
        {
            var image = (controller.view != null) ? controller.view.monsterImage : null;
            if (image == null)
            {
                // Fallback to recursive find if view is not initialized
                var monsterArea = FindTransformRecursive(controller.transform, "MonsterArea");
                if (monsterArea != null)
                {
                    var imageTransform = monsterArea.Find("Image");
                    if (imageTransform != null)
                    {
                        image = imageTransform.GetComponent<Image>();
                    }
                }
            }

            KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] ApplyCustomSizingToMenuImage: target Image component found={image != null}");

            if (image != null && image.rectTransform != null)
            {
                int targetW = metadata.Width > 0 ? metadata.Width : (int)sprite.rect.width;
                int targetH = metadata.Height > 0 ? metadata.Height : (int)sprite.rect.height;

                KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] Applying sizing to Bestiary Image: Sprite={sprite.name}, TargetSize={targetW}x{targetH}, Offset=({metadata.OffsetX}, {metadata.OffsetY})");

                if (metadata.Width > 0 || metadata.Height > 0)
                {
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);
                }

                if (metadata.OffsetX.HasValue || metadata.OffsetY.HasValue)
                {
                    float ox = metadata.OffsetX ?? 0f;
                    float oy = metadata.OffsetY ?? 0f;
                    image.rectTransform.anchoredPosition = new Vector2(ox, oy);
                }
            }
        }
    }

    internal static Transform FindTransformRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var result = FindTransformRecursive(parent.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }
}
