using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Battle;
using UnityEngine;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class BattleDiagnosticPatch
{
    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.GetEnemySprite))]
    [HarmonyPostfix]
    private static void GetEnemySpritePostfix(ref Sprite __result)
    {
        if (__result == null)
        {
            KupoUIPRPlugin.PluginLog.LogInfo("[BattleDiagnostic] GetEnemySprite returned null");
            return;
        }

        var texName = __result.texture != null ? __result.texture.name : "null";
        var texSize = __result.texture != null ? $"{__result.texture.width}x{__result.texture.height}" : "n/a";
        
        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[BattleDiagnostic] GetEnemySprite (Original): Name={__result.name}, " +
            $"TextureName={texName}, " +
            $"TextureSize={texSize}, " +
            $"SpriteRect={__result.rect.width}x{__result.rect.height} (at x={__result.rect.x}, y={__result.rect.y}), " +
            $"PPU={__result.pixelsPerUnit}");

        if (__result.name.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Ensure it is not permanently skipped/cached as missed
        KupoUI.PR.Textures.TextureResolver.RemoveSpriteFromSkippedCache(__result.GetInstanceID());

        KupoUI.PR.Textures.AssetAddressTracker.TryGetAddress(__result, __result.texture, out var assetAddress);
        if (KupoUI.PR.Textures.TextureResolver.TryCreateReplacementSprite(__result, out var replacement, assetAddress))
        {
            __result = replacement;
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] GetEnemySprite: Substituted replacement sprite {replacement.name} (Rect: {replacement.rect.width}x{replacement.rect.height})");
        }
    }


    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.InitEnemyEntity))]
    [HarmonyPostfix]
    private static void InitEnemyEntityPostfix(BattleEnemyEntity battleEnemyEntity, int id)
    {
        if (battleEnemyEntity == null)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] InitEnemyEntity: entity is null, id={id}");
            return;
        }

        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] InitEnemyEntity: id={id}, EntityName={battleEnemyEntity.name}");

        try
        {
            // Walk all renderers in the hierarchy
            var renderers = battleEnemyEntity.GetComponentsInChildren<Renderer>(true);
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Found {renderers.Length} Renderer components:");
            foreach (var r in renderers)
            {
                if (r == null) continue;
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]     - [{r.name}] Type={r.GetIl2CppType().Name}, Enabled={r.enabled}");
            }

            // Walk all SpriteRenderers in the hierarchy
            var srs = battleEnemyEntity.GetComponentsInChildren<SpriteRenderer>(true);
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Found {srs.Length} SpriteRenderer components:");
            foreach (var sr in srs)
            {
                if (sr == null) continue;
                var sprite = sr.sprite;
                var spriteName = sprite != null ? sprite.name : "null";
                var rectSize = sprite != null ? $"{sprite.rect.width}x{sprite.rect.height}" : "n/a";
                var ppu = sprite != null ? sprite.pixelsPerUnit.ToString() : "n/a";
                var texSize = (sprite != null && sprite.texture != null) ? $"{sprite.texture.width}x{sprite.texture.height}" : "n/a";
                
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[BattleDiagnostic]     - SpriteRenderer [{sr.name}]: " +
                    $"Sprite={spriteName}, " +
                    $"SpriteRect={rectSize}, " +
                    $"PPU={ppu}, " +
                    $"TexSize={texSize}");
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic] Error walking entity hierarchy: {ex.Message}");
        }
    }

    private static readonly Dictionary<IntPtr, Tuple<int, int>> CustomSizes = new();
    private static readonly Dictionary<IntPtr, (Vector2 facePos, Vector2 mouthPos)> SavedPositions = new();
    private static bool _isRecallingSetSize = false;

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.SetSprite))]
    [HarmonyPostfix]
    private static void SetSpritePostfix(BattleSpriteEntity __instance, Sprite sprite)
    {
        if (sprite == null) return;
        
        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] SetSprite called on {__instance.name} (ptr={__instance.Pointer:X}) with sprite {sprite.name}");

        // Look up metadata for the custom sprite (strip the _Custom suffix if present)
        var spriteName = sprite.name;
        if (spriteName.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            spriteName = spriteName.Substring(0, spriteName.Length - 7);
        }

        KupoUI.PR.Textures.AssetAddressTracker.TryGetAddress(sprite, sprite.texture, out var assetAddress);
        var normalizedName = KupoUI.PR.Textures.TextureResolver.NormalizeName(spriteName);
        string filePath;
        
        if (!string.IsNullOrEmpty(assetAddress) && KupoUI.PR.Textures.TextureResolver.TryGetFilePathByAddress(assetAddress, out var addressedPath))
        {
            filePath = addressedPath;
        }
        else if (KupoUI.PR.Textures.TextureResolver.TryGetFilePathByNormalizedName(normalizedName, out var normalPath))
        {
            filePath = normalPath;
        }
        else
        {
            filePath = null;
        }

        if (filePath != null)
        {
            var metadata = KupoUI.PR.Textures.TextureResolver.LoadTextureMetadata(filePath);
            if (metadata != null && (metadata.Width > 0 || metadata.Height > 0))
            {
                int customW = metadata.Width > 0 ? metadata.Width : (int)sprite.rect.width;
                int customH = metadata.Height > 0 ? metadata.Height : (int)sprite.rect.height;
                
                CustomSizes[__instance.Pointer] = new Tuple<int, int>(customW, customH);
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Stored custom size {customW}x{customH} for BattleSpriteEntity {__instance.name} (ptr={__instance.Pointer:X})");

                Vector2 facePos = Vector2.zero;
                Vector2 mouthPos = Vector2.zero;

                if (SavedPositions.TryGetValue(__instance.Pointer, out var saved))
                {
                    facePos = saved.facePos;
                    mouthPos = saved.mouthPos;
                }

                if (!_isRecallingSetSize)
                {
                    _isRecallingSetSize = true;
                    try
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Re-calling SetSize with custom size: {customW}x{customH} for ptr={__instance.Pointer:X}");
                        __instance.SetSize(customW, customH, facePos, mouthPos);
                    }
                    catch (Exception ex)
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic]   Failed to re-call SetSize: {ex.Message}");
                    }
                    finally
                    {
                        _isRecallingSetSize = false;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.Init))]
    [HarmonyPrefix]
    private static void InitPrefix(BattleSpriteEntity __instance, ref int width, ref int height, Vector2 facePosition, Vector2 mouthPosition, bool isPlayer)
    {
        if (_isRecallingSetSize) return;

        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Init called on {__instance.name} (ptr={__instance.Pointer:X}) with original width={width}, height={height}");
        SavedPositions[__instance.Pointer] = (facePosition, mouthPosition);

        if (CustomSizes.TryGetValue(__instance.Pointer, out var customSize))
        {
            width = customSize.Item1;
            height = customSize.Item2;
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding Init size to custom: {width}x{height}");
        }
    }

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.SetSize))]
    [HarmonyPrefix]
    private static void SetSizePrefix(BattleSpriteEntity __instance, ref int width, ref int height, Vector2 facePosition, Vector2 mouthPosition)
    {
        if (_isRecallingSetSize) return;

        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] SetSize called on {__instance.name} (ptr={__instance.Pointer:X}) with original width={width}, height={height}");
        SavedPositions[__instance.Pointer] = (facePosition, mouthPosition);

        if (CustomSizes.TryGetValue(__instance.Pointer, out var customSize))
        {
            width = customSize.Item1;
            height = customSize.Item2;
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding SetSize size to custom: {width}x{height}");
        }
    }

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.OnDestroy))]
    [HarmonyPrefix]
    private static void OnDestroyPrefix(BattleSpriteEntity __instance)
    {
        CustomSizes.Remove(__instance.Pointer);
        SavedPositions.Remove(__instance.Pointer);
        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] OnDestroy: Cleared custom size mapping for BattleSpriteEntity {__instance.name} (ptr={__instance.Pointer:X})");
    }
}
