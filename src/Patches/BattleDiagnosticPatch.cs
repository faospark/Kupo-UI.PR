using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Last.Battle;
using Last.Data.Master;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class BattleDiagnosticPatch
{
    private static readonly Dictionary<int, string> AssetIdToSpriteName = new();
    private static readonly Dictionary<IntPtr, Tuple<int, int>> CustomSizes = new();
    private static readonly Dictionary<IntPtr, (Vector2 facePos, Vector2 mouthPos)> SavedPositions = new();
    private static Tuple<int, int> _currentCustomSize = null;
    private static bool _isRecallingSetSize = false;

    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.CreateEnemy))]
    [HarmonyPrefix]
    private static void CreateEnemyPrefix(Monster monster)
    {
        if (monster == null) return;
        
        _currentCustomSize = null;

        int assetId = monster.MonsterAssetId;
        if (AssetIdToSpriteName.TryGetValue(assetId, out var spriteName))
        {
            // Resolve metadata for this sprite name
            var metadata = KupoUI.PR.Textures.TextureResolver.LoadMetadataByName(spriteName);
            if (metadata != null)
            {
                int customW = metadata.SpriteWidth > 0 ? metadata.SpriteWidth : (metadata.Width > 0 ? metadata.Width : 0);
                int customH = metadata.SpriteHeight > 0 ? metadata.SpriteHeight : (metadata.Height > 0 ? metadata.Height : 0);
                if (customW > 0 && customH > 0)
                {
                    _currentCustomSize = new Tuple<int, int>(customW, customH);
                    
                    try
                    {
                        var monsterType = monster.GetType();
                        var widthProp = monsterType.GetProperty("Width", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) 
                                        ?? monsterType.GetProperty("width", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        var heightProp = monsterType.GetProperty("Height", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                         ?? monsterType.GetProperty("height", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                        if (widthProp != null && widthProp.CanWrite)
                        {
                            widthProp.SetValue(monster, customW);
                        }
                        if (heightProp != null && heightProp.CanWrite)
                        {
                            heightProp.SetValue(monster, customH);
                        }

                        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                        {
                            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Dynamically set monster size properties (Width={widthProp?.Name}, Height={heightProp?.Name}) to {customW}x{customH}");
                            // Also print all properties to help debug if they are missing
                            foreach (var p in monsterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Monster Property: {p.Name} (Type={p.PropertyType.Name}, CanWrite={p.CanWrite})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic] Failed to set monster properties via reflection: {ex.Message}");
                    }

                    if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Stored custom size {customW}x{customH} for upcoming enemy creation of asset ID {assetId}");
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.CreateEnemy))]
    [HarmonyPostfix]
    private static void CreateEnemyPostfix(BattleEnemyEntity __result)
    {
        _currentCustomSize = null;
    }

    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.GetEnemySprite))]
    [HarmonyPostfix]
    private static void GetEnemySpritePostfix(ref Sprite __result, Il2CppSystem.Object monsterAsset)
    {
        if (monsterAsset != null)
        {
            try
            {
                var idProp = monsterAsset.GetType().GetProperty("Id");
                var groupProp = monsterAsset.GetType().GetProperty("GroupName");
                var assetProp = monsterAsset.GetType().GetProperty("AssetName");

                if (idProp != null && groupProp != null && assetProp != null)
                {
                    int assetId = (int)idProp.GetValue(monsterAsset);
                    string groupName = (string)groupProp.GetValue(monsterAsset);
                    string assetName = (string)assetProp.GetValue(monsterAsset);

                    if (!string.IsNullOrEmpty(groupName) && !string.IsNullOrEmpty(assetName))
                    {
                        string cleanGroup = groupName.Trim().ToUpperInvariant();
                        string cleanAsset = assetName.Trim().ToUpperInvariant();
                        string computedSpriteName = cleanAsset.StartsWith(cleanGroup, StringComparison.Ordinal)
                            ? cleanAsset
                            : $"{cleanGroup}_{cleanAsset}";
                        AssetIdToSpriteName[assetId] = computedSpriteName;
                        
                        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                        {
                            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Mapped MonsterAssetId {assetId} to SpriteName {computedSpriteName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic] Failed to build asset mapping: {ex.Message}");
            }
        }

        if (__result == null)
        {
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo("[BattleDiagnostic] GetEnemySprite returned null");
            }
            return;
        }

        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            var texName = __result.texture != null ? __result.texture.name : "null";
            var texSize = __result.texture != null ? $"{__result.texture.width}x{__result.texture.height}" : "n/a";
            
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[BattleDiagnostic] GetEnemySprite (Original): Name={__result.name}, " +
                $"TextureName={texName}, " +
                $"TextureSize={texSize}, " +
                $"SpriteRect={__result.rect.width}x{__result.rect.height} (at x={__result.rect.x}, y={__result.rect.y}), " +
                $"PPU={__result.pixelsPerUnit}");
        }

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
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] GetEnemySprite: Substituted replacement sprite {replacement.name} (Rect: {replacement.rect.width}x{replacement.rect.height})");
            }
        }
    }

    [HarmonyPatch(typeof(InstantiateManager), nameof(InstantiateManager.InitEnemyEntity))]
    [HarmonyPostfix]
    private static void InitEnemyEntityPostfix(BattleEnemyEntity battleEnemyEntity, int id)
    {
        if (!KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value) return;

        if (battleEnemyEntity == null)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] InitEnemyEntity: entity is null, id={id}");
            return;
        }

        KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] InitEnemyEntity: id={id}, EntityName={battleEnemyEntity.name}");

        try
        {
            // Walk all renderers in the hierarchy and inspect Mesh properties
            var renderers = battleEnemyEntity.GetComponentsInChildren<Renderer>(true);
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Found {renderers.Length} Renderer components:");
            foreach (var r in renderers)
            {
                if (r == null) continue;
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]     - [{r.name}] Type={r.GetIl2CppType().Name}, Enabled={r.enabled}");

                if (r.name == "Mesh")
                {
                    var t = r.transform;
                    KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]       Mesh Transform: localPosition={t.localPosition}, localScale={t.localScale}, lossyScale={t.lossyScale}");
                    
                    // Trace hierarchy path
                    string path = t.name;
                    var parent = t.parent;
                    while (parent != null)
                    {
                        path = parent.name + "/" + path;
                        parent = parent.parent;
                    }
                    KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]       Mesh full path: {path}");
                }
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

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.SetSprite))]
    [HarmonyPostfix]
    private static void SetSpritePostfix(BattleSpriteEntity __instance, Sprite sprite)
    {
        if (__instance == null || __instance.Pointer == IntPtr.Zero || sprite == null) return;
        
        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] SetSprite called on {__instance.name} (ptr={__instance.Pointer:X}) with sprite {sprite.name}");
        }

        // Look up metadata for the custom sprite (strip the _Custom suffix if present)
        var spriteName = sprite.name;
        if (spriteName.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
        {
            spriteName = spriteName.Substring(0, spriteName.Length - 7);
        }

        KupoUI.PR.Textures.AssetAddressTracker.TryGetAddress(sprite, sprite.texture, out var assetAddress);
        var metadata = CustomTexturePatch.GetMetadataForSprite(sprite, assetAddress);
        
        if (metadata != null)
        {
            int customW = metadata.SpriteWidth > 0 ? metadata.SpriteWidth : (metadata.Width > 0 ? metadata.Width : (int)sprite.rect.width);
            int customH = metadata.SpriteHeight > 0 ? metadata.SpriteHeight : (metadata.Height > 0 ? metadata.Height : (int)sprite.rect.height);
            
            CustomSizes[__instance.Pointer] = new Tuple<int, int>(customW, customH);
                if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Stored custom size {customW}x{customH} for BattleSpriteEntity {__instance.name} (ptr={__instance.Pointer:X})");
                }

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
                        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                        {
                            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Re-calling SetSize with custom size: {customW}x{customH} for ptr={__instance.Pointer:X}");
                        }
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
                // Apply offsets directly to the Mesh vertices
                if (metadata.ResolvedOffsetX.HasValue || metadata.ResolvedOffsetY.HasValue)
                {
                    Transform meshTransform = null;
                    var children = __instance.GetComponentsInChildren<Transform>(true);
                    foreach (var child in children)
                    {
                        if (child != null && child.name == "Mesh")
                        {
                            meshTransform = child;
                            break;
                        }
                    }

                    if (meshTransform != null)
                    {
                        var filter = meshTransform.GetComponent<MeshFilter>();
                        if (filter != null && filter.mesh != null)
                        {
                            float ox = metadata.ResolvedOffsetX ?? 0f;
                            float oy = metadata.ResolvedOffsetY ?? 0f;
                            
                            var vertices = filter.mesh.vertices;
                            for (int i = 0; i < vertices.Length; i++)
                            {
                                vertices[i] = new Vector3(vertices[i].x + ox, vertices[i].y + oy, vertices[i].z);
                            }
                            filter.mesh.vertices = vertices;
                            filter.mesh.RecalculateBounds();
                            
                            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Shifted {vertices.Length} mesh vertices by ({ox}, {oy}) for ptr={__instance.Pointer:X}");
                            }
                        }
                        else
                        {
                            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                            {
                                KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic] MeshFilter or Mesh is null for ptr={__instance.Pointer:X}");
                            }
                        }
                    }
                    else
                    {
                        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning($"[BattleDiagnostic] Mesh transform child not found for ptr={__instance.Pointer:X}");
                        }
                    }
                }
            }
        }

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.Init))]
    [HarmonyPrefix]
    private static void InitPrefix(BattleSpriteEntity __instance, ref int width, ref int height, Vector2 facePosition, Vector2 mouthPosition, bool isPlayer)
    {
        if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
        if (_isRecallingSetSize) return;

        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] Init called on {__instance.name} (ptr={__instance.Pointer:X}) with original width={width}, height={height}");
        }
        SavedPositions[__instance.Pointer] = (facePosition, mouthPosition);

        if (_currentCustomSize != null)
        {
            width = _currentCustomSize.Item1;
            height = _currentCustomSize.Item2;
            CustomSizes[__instance.Pointer] = _currentCustomSize;
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding Init size to custom (creating): {width}x{height}");
            }
        }
        else if (CustomSizes.TryGetValue(__instance.Pointer, out var customSize))
        {
            width = customSize.Item1;
            height = customSize.Item2;
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding Init size to custom (instance): {width}x{height}");
            }
        }
    }

    [HarmonyPatch(typeof(BattleSpriteEntity), nameof(BattleSpriteEntity.SetSize))]
    [HarmonyPrefix]
    private static void SetSizePrefix(BattleSpriteEntity __instance, ref int width, ref int height, Vector2 facePosition, Vector2 mouthPosition)
    {
        if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
        if (_isRecallingSetSize) return;

        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic] SetSize called on {__instance.name} (ptr={__instance.Pointer:X}) with original width={width}, height={height}");
        }
        SavedPositions[__instance.Pointer] = (facePosition, mouthPosition);

        if (_currentCustomSize != null)
        {
            width = _currentCustomSize.Item1;
            height = _currentCustomSize.Item2;
            CustomSizes[__instance.Pointer] = _currentCustomSize;
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding SetSize size to custom (creating): {width}x{height}");
            }
        }
        else if (CustomSizes.TryGetValue(__instance.Pointer, out var customSize))
        {
            width = customSize.Item1;
            height = customSize.Item2;
            if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[BattleDiagnostic]   Overriding SetSize size to custom (instance): {width}x{height}");
            }
        }
    }

    // Clear per-entity lookup tables on every scene load instead of hooking
    // BattleSpriteEntity.OnDestroy — the game's own OnDestroy body throws a
    // NullReferenceException on pre-pooled/uninitialized entities during the
    // intro scene, and our Harmony prefix causes that body to still be invoked.
    [HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
    [HarmonyPostfix]
    private static void SceneLoadedPostfix()
    {
        CustomSizes.Clear();
        SavedPositions.Clear();
        if (KupoUIPRPlugin.DiagnosticBattleLoggingConfig.Value)
        {
            KupoUIPRPlugin.PluginLog.LogInfo("[BattleDiagnostic] Scene loaded: cleared CustomSizes and SavedPositions.");
        }
    }
}
