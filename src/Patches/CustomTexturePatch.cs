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
        if (!KupoUIPRPlugin.EnableCustomTextures || __result == null || __result.name.EndsWith("_Custom"))
        {
            return;
        }

        // Add spriteId to processed set immediately — HashSet.Add returns false if already present.
        // This prevents any recursive re-entry during rendering or layout updates.
        var spriteId = __instance.GetInstanceID();
        if (!SpriteTextureProcessedIds.Add(spriteId))
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
            return;
        }

        TextureLogger.LogObservedTextureName(__result.name, "Sprite.texture.get");

        bool replaced = TextureResolver.TryReplaceTextureInPlace(__result, __result.name, assetAddress);

        if (KupoUIPRPlugin.DiagnosticTextureTilingConfig != null && KupoUIPRPlugin.DiagnosticTextureTilingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[WrapModeDiag] SpriteTexturePostfix: sprite='{__instance?.name}', tex='{__result?.name}', replaced={replaced}, isTiling={TextureResolver.IsAnyAxisTiling(__result)}");
        }
    }

    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void SpriteRendererSpritePrefix(SpriteRenderer __instance, ref Sprite value)
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

        if (__instance != null && value != null && value.texture != null && TextureResolver.IsAnyAxisTiling(value.texture))
        {
            __instance.drawMode = SpriteDrawMode.Tiled;
        }
    }

    [HarmonyPatch(typeof(Image), nameof(Image.overrideSprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void UIImageOverrideSpritePrefix(Image __instance, ref Sprite value)
    {
        UIImageSpritePrefix(__instance, ref value);
    }

    internal static bool IsCustomTilingActive(Sprite sprite, string assetAddress = null)
    {
        if (sprite == null) return false;

        var metadata = GetMetadataForSprite(sprite, assetAddress);
        if (metadata != null)
        {
            return TextureResolver.IsAnyAxisTiling(metadata);
        }

        if (sprite.name != null && sprite.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase) && sprite.texture != null)
        {
            return TextureResolver.IsAnyAxisTiling(sprite.texture);
        }

        return false;
    }

    internal static bool IsCustomTilingActiveForTexture(Texture texture, string assetAddress = null)
    {
        if (texture == null || !(texture is Texture2D tex2d)) return false;

        var metadata = TextureResolver.LoadMetadataByName(tex2d.name);
        if (metadata != null)
        {
            return TextureResolver.IsAnyAxisTiling(metadata);
        }

        if (tex2d.name != null && tex2d.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            return TextureResolver.IsAnyAxisTiling(tex2d);
        }

        return false;
    }

    [HarmonyPatch(typeof(Image), nameof(Image.type), MethodType.Setter)]
    [HarmonyPrefix]
    private static void UIImageTypePrefix(Image __instance, ref Image.Type value)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures || __instance == null) return;
        var activeSprite = __instance.overrideSprite ?? __instance.sprite;
        if (IsCustomTilingActive(activeSprite))
        {
            if (value != Image.Type.Tiled)
            {
                value = Image.Type.Tiled;
                __instance.preserveAspect = false;
            }
        }
    }

    [HarmonyPatch(typeof(Image), "OnPopulateMesh")]
    [HarmonyPrefix]
    private static void ImageOnPopulateMeshPrefix(Image __instance)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures || __instance == null) return;
        var activeSprite = __instance.overrideSprite ?? __instance.sprite;
        if (IsCustomTilingActive(activeSprite))
        {
            if (__instance.type != Image.Type.Tiled)
            {
                __instance.type = Image.Type.Tiled;
                __instance.preserveAspect = false;
            }
        }
    }

    [HarmonyPatch(typeof(RawImage), "OnPopulateMesh")]
    [HarmonyPrefix]
    private static void RawImageOnPopulateMeshPrefix(RawImage __instance)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures || __instance == null) return;
        if (__instance.texture is Texture2D tex2d && IsCustomTilingActiveForTexture(tex2d))
        {
            TextureResolver.ApplyRawImageTiling(__instance, tex2d);
        }
    }

    [HarmonyPatch(typeof(RawImage), nameof(RawImage.texture), MethodType.Setter)]
    [HarmonyPrefix]
    private static void RawImageTexturePrefix(RawImage __instance, ref Texture value)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures || __instance == null || value == null) return;

        if (value is Texture2D tex2d)
        {
            AssetAddressTracker.TryGetAddress(null, tex2d, out var assetAddress);
            TextureResolver.TryReplaceTextureInPlace(tex2d, tex2d.name, assetAddress);

            if (IsCustomTilingActiveForTexture(tex2d, assetAddress))
            {
                TextureResolver.ApplyRawImageTiling(__instance, tex2d);
                if (KupoUIPRPlugin.DiagnosticTextureTilingConfig != null && KupoUIPRPlugin.DiagnosticTextureTilingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[WrapModeDiag] RawImageTexturePrefix set uvRect on '{__instance.name}' (texture='{tex2d.name}') -> {__instance.uvRect}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    [HarmonyPrefix]
    private static void UIImageSpritePrefix(Image __instance, ref Sprite value)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures) return;
        if (value == null) return;

        bool isAlreadyCustom = value.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase);

        AssetAddressTracker.TryGetAddress(value, value.texture, out var assetAddress);

        if (KupoUIPRPlugin.DiagnosticTextureTilingConfig != null && KupoUIPRPlugin.DiagnosticTextureTilingConfig.Value)
        {
            TryBuildTransformPath(__instance.transform, out var pathStr);
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[WrapModeDiag] UIImageSpritePrefix: Image='{__instance.name}' (path='{pathStr}'), sprite='{value.name}', typeBefore={__instance.type}, isAlreadyCustom={isAlreadyCustom}");
        }

        // Add explicit trace for Bestiary image component
        if (__instance.name == "Image" && KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[CustomTexturePatch] Image.sprite setter prefix: name={__instance.name}, spriteName={value.name}, textureName={value.texture?.name}, address={assetAddress}");
        }

        if (!isAlreadyCustom)
        {
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

                // Print all sibling children under parent (MonsterArea)
                if (__instance.transform.parent != null)
                {
                    var parentTrans = __instance.transform.parent;
                    for (int i = 0; i < parentTrans.childCount; i++)
                    {
                        var child = parentTrans.GetChild(i);
                        var childComps = child.GetComponents<UnityEngine.Component>();
                        var compNames = new List<string>();
                        foreach (var c in childComps)
                        {
                            if (c != null) compNames.Add(c.GetType().FullName);
                        }
                        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] Sibling Child {i}: {child.name} | active={child.gameObject.activeSelf} | scale={child.localScale} | comps={string.Join(", ", compNames)}");
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
                // For the Bestiary/Library monster image, use Simple type unless the texture
                // has a tiling wrap mode (in which case Tiled will be applied at the end).
                if (__instance.name == "Image" && !TextureResolver.IsAnyAxisTiling(value?.texture))
                {
                    __instance.preserveAspect = false; // Disable to allow custom aspect ratio!
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

                int targetW = metadata.SpriteWidth > 0 ? metadata.SpriteWidth : (metadata.Width > 0 ? metadata.Width : (int)value.rect.width);
                int targetH = metadata.SpriteHeight > 0 ? metadata.SpriteHeight : (metadata.Height > 0 ? metadata.Height : (int)value.rect.height);

                if (metadata.SpriteWidth > 0 || metadata.SpriteHeight > 0 || metadata.Width > 0 || metadata.Height > 0)
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

                if (metadata.ResolvedOffsetX.HasValue || metadata.ResolvedOffsetY.HasValue)
                {
                    if (__instance.rectTransform != null)
                    {
                        float ox = metadata.ResolvedOffsetX ?? 0f;
                        float oy = metadata.ResolvedOffsetY ?? 0f;
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

        // Final override: if the texture has a tiling wrap mode, ensure the Image is Tiled.
        // This runs last so no earlier metadata path can reset it back to Simple.
        if (IsCustomTilingActive(value, assetAddress))
        {
            var oldType = __instance.type;
            __instance.type = UnityEngine.UI.Image.Type.Tiled;
            // preserveAspect is irrelevant (and misleading) for Tiled type — clear it.
            __instance.preserveAspect = false;

            if (KupoUIPRPlugin.DiagnosticTextureTilingConfig != null && KupoUIPRPlugin.DiagnosticTextureTilingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[WrapModeDiag]   Final override set Image '{__instance.name}' type from {oldType} -> Tiled (texture='{value.texture.name}', wrapMode={value.texture.wrapMode}, u={value.texture.wrapModeU}, v={value.texture.wrapModeV})");
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

        if (!string.IsNullOrEmpty(assetAddress) && TextureResolver.TryGetFilePathByAddress(assetAddress, out var addressedPath))
        {
            return TextureResolver.LoadTextureMetadata(addressedPath);
        }

        return TextureResolver.LoadMetadataByNormalizedName(normalizedName);
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

        if (image != null && image.sprite != null && !image.sprite.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            AssetAddressTracker.TryGetAddress(image.sprite, image.sprite.texture, out var assetAddress);
            if (TextureResolver.TryCreateReplacementSprite(image.sprite, out var replacement, assetAddress, isUi: true))
            {
                image.sprite = replacement;
                sprite = replacement;
            }
        }

        // Retrieve metadata for this sprite name
        var metadata = GetMetadataForSprite(sprite, null);
        KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] ApplyCustomSizingToMenuImage: sprite={sprite.name}, hasMetadata={metadata != null}");

        if (metadata != null)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] ApplyCustomSizingToMenuImage: target Image component found={image != null}");

            if (image != null && image.rectTransform != null)
            {
                int targetW = metadata.SpriteWidth > 0 ? metadata.SpriteWidth : (metadata.Width > 0 ? metadata.Width : (int)sprite.rect.width);
                int targetH = metadata.SpriteHeight > 0 ? metadata.SpriteHeight : (metadata.Height > 0 ? metadata.Height : (int)sprite.rect.height);

                KupoUIPRPlugin.PluginLog.LogWarning($"[BestiaryDiag] Applying sizing to Bestiary Image: Sprite={sprite.name}, TargetSize={targetW}x{targetH}, Offset=({metadata.ResolvedOffsetX}, {metadata.ResolvedOffsetY})");

                if (metadata.SpriteWidth > 0 || metadata.SpriteHeight > 0 || metadata.Width > 0 || metadata.Height > 0)
                {
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);
                    image.preserveAspect = false;
                }

                if (metadata.ResolvedOffsetX.HasValue || metadata.ResolvedOffsetY.HasValue)
                {
                    float ox = metadata.ResolvedOffsetX ?? 0f;
                    float oy = metadata.ResolvedOffsetY ?? 0f;
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

    [HarmonyPatch(typeof(RectTransform), nameof(RectTransform.sizeDelta), MethodType.Setter)]
    [HarmonyPrefix]
    private static void RectTransformSizeDeltaPrefix(RectTransform __instance, ref Vector2 value)
    {
        if (!KupoUIPRPlugin.EnableCustomTextures) return;
        if (__instance == null) return;

        if (__instance.name == "Image" && __instance.transform.parent != null && __instance.transform.parent.name == "MonsterArea")
        {
            var image = __instance.GetComponent<Image>();
            if (image != null && image.sprite != null)
            {
                var sprite = image.sprite;
                var metadata = GetMetadataForSprite(sprite, null);
                if (metadata != null)
                {
                    float customW = metadata.SpriteWidth > 0 ? metadata.SpriteWidth : (metadata.Width > 0 ? metadata.Width : 0);
                    float customH = metadata.SpriteHeight > 0 ? metadata.SpriteHeight : (metadata.Height > 0 ? metadata.Height : 0);

                    if (customW > 0f && customH > 0f && value.y > 0f)
                    {
                        float aspect = customW / customH;
                        value = new Vector2(value.y * aspect, value.y);
                    }
                }
            }
        }
    }
}
