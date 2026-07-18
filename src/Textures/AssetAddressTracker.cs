using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Management;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Textures;

/// <summary>
/// Tracks Unity addressable asset addresses by their IL2CPP native pointer.
/// Hooks ResourceManager.IsLoadAssetCompleted to record which address each
/// loaded sprite/texture object was loaded from. This enables path-based
/// texture resolution for assets under the GameAssets hierarchy, preventing
/// filename collisions across different bundles.
/// </summary>
[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.IsLoadAssetCompleted))]
internal static class AssetAddressTracker
{
    // Maps IL2CPP native object pointer → addressable asset address
    // e.g. 0x1234abcd → "Assets/GameAssets/Serial/Res/UI/FF2/Portrait/CharaFace"
    private static readonly Dictionary<IntPtr, string> AddressByPointer =
        new Dictionary<IntPtr, string>();

    /// <summary>
    /// Attempts to retrieve the addressable asset address for a given IL2CPP native pointer.
    /// </summary>
    internal static bool TryGetAddress(IntPtr nativePointer, out string assetAddress)
    {
        if (nativePointer == IntPtr.Zero)
        {
            assetAddress = null;
            return false;
        }

        return AddressByPointer.TryGetValue(nativePointer, out assetAddress);
    }

    internal static bool TryGetAddress(Sprite sprite, Texture2D texture, out string assetAddress)
    {
        assetAddress = null;

        if (sprite != null && TryGetAddress(sprite.Pointer, out assetAddress))
        {
            return true;
        }

        if (texture != null && TryGetAddress(texture.Pointer, out assetAddress))
        {
            return true;
        }

        return false;
    }

    [HarmonyPostfix]
    // Harmony injects: addressName = original param, __result = return value
    private static void Postfix(string addressName, bool __result)
    {
        if (!__result || string.IsNullOrEmpty(addressName))
        {
            return;
        }

        // Only track GameAssets entries; everything else is irrelevant for path-based
        // resolution and we want to keep the dictionary small.
        if (addressName.IndexOf("GameAssets", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        try
        {
            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Postfix called for addressName: {addressName}");
            }

            var instance = ResourceManager.Instance;
            if (instance == null)
            {
                return;
            }

            var dic = instance.completeAssetDic;
            if (dic == null)
            {
                if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning($"[AssetAddressTracker] completeAssetDic is null");
                }
                return;
            }

            if (!dic.ContainsKey(addressName))
            {
                if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning($"[AssetAddressTracker] completeAssetDic does not contain key: {addressName}");
                }
                return;
            }

            var asset = dic[addressName];
            if (asset == null)
            {
                if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning($"[AssetAddressTracker] asset is null in completeAssetDic for: {addressName}");
                }
                return;
            }

            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Found asset in completeAssetDic: {addressName}, Type: {asset.GetIl2CppType().FullName}");
            }

            AddressByPointer[asset.Pointer] = addressName;

            var sprite = asset.TryCast<Sprite>();
            var go = asset.TryCast<GameObject>();

            // UI code can receive cloned Sprite instances later, but they usually still point
            // at the original texture. Index the texture pointer as well so path-based
            // resolution survives sprite cloning/instantiation.
            if (sprite != null && sprite.texture != null)
            {
                AddressByPointer[sprite.texture.Pointer] = addressName;
            }
            else if (go != null)
            {
                if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                {
                    try
                    {
                        var comps = go.GetComponentsInChildren<Component>(true);
                        var compNames = new List<string>();
                        if (comps != null)
                        {
                            foreach (var c in comps)
                            {
                                if (c != null)
                                {
                                    compNames.Add(c.GetIl2CppType().Name);
                                }
                            }
                        }
                        KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Prefab '{addressName}' has components: {string.Join(", ", compNames)}");
                    }
                    catch
                    {
                        // Ignore log errors
                    }
                }

                var components = go.GetComponentsInChildren<Component>(true);
                if (components != null)
                {
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;

                        var sr = comp.TryCast<SpriteRenderer>();
                        var img = comp.TryCast<Image>();
                        var rawImg = comp.TryCast<RawImage>();
                        var r = comp.TryCast<Renderer>();

                        if (sr != null)
                        {
                            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Processing SpriteRenderer. Sprite is null: {sr.sprite == null}");
                                if (sr.sprite != null)
                                {
                                    KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] SpriteName: '{sr.sprite.name}', Texture is null: {sr.sprite.texture == null}");
                                }
                            }
                            RegisterSprite(sr.sprite, addressName);
                        }
                        else if (img != null)
                        {
                            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Processing UI Image. Sprite is null: {img.sprite == null}");
                            }
                            RegisterSprite(img.sprite, addressName);
                        }
                        else if (rawImg != null)
                        {
                            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Processing UI RawImage. Texture is null: {rawImg.texture == null}");
                            }
                            RegisterTexture(rawImg.texture, addressName);
                        }
                        else if (r != null)
                        {
                            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                            {
                                KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Processing Renderer. Type: {r.GetIl2CppType().Name}, Materials count: {r.sharedMaterials?.Length ?? 0}");
                            }
                            var sharedMats = r.sharedMaterials;
                            if (sharedMats != null)
                            {
                                foreach (var mat in sharedMats)
                                {
                                    if (mat != null)
                                    {
                                        if (KupoUIPRPlugin.IsTextureLoggerEnabled)
                                        {
                                            KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Material mainTexture is null: {mat.mainTexture == null}");
                                        }
                                        if (mat.mainTexture != null)
                                        {
                                            RegisterTexture(mat.mainTexture, addressName);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            ScanComponentProperties(comp, addressName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (KupoUIPRPlugin.IsTextureLoggerEnabled)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[AssetAddressTracker] Error in Postfix: {ex.Message}");
            }
        }
    }

    private static void RegisterSprite(Sprite sprite, string addressName)
    {
        if (sprite == null)
        {
            return;
        }

        var name = TextureResolver.NormalizeName(sprite.name);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var subAddress = GetSubAddress(addressName, name);
        AddressByPointer[sprite.Pointer] = subAddress;

        if (KupoUIPRPlugin.IsTextureLoggerEnabled)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Indexed Sprite pointer: {sprite.Pointer} -> {subAddress} (Source: {addressName})");
        }

        if (sprite.texture != null)
        {
            RegisterTexture(sprite.texture, addressName);
        }
    }

    private static void RegisterTexture(Texture texture, string addressName)
    {
        if (texture == null)
        {
            return;
        }

        var name = TextureResolver.NormalizeName(texture.name);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var subAddress = GetSubAddress(addressName, name);
        AddressByPointer[texture.Pointer] = subAddress;

        if (KupoUIPRPlugin.IsTextureLoggerEnabled)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[AssetAddressTracker] Indexed Texture pointer: {texture.Pointer} -> {subAddress} (Source: {addressName})");
        }
    }

    private static string GetSubAddress(string addressName, string name)
    {
        if (string.IsNullOrEmpty(addressName))
        {
            return name;
        }

        var path = addressName.Replace('\\', '/');
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return $"{path}/{name}";
        }

        var prefabFileNameWithExt = path.Substring(lastSlash + 1);
        var prefabFileName = prefabFileNameWithExt;
        var extDot = prefabFileNameWithExt.LastIndexOf('.');
        if (extDot > 0)
        {
            prefabFileName = prefabFileNameWithExt.Substring(0, extDot);
        }

        var parentPath = path.Substring(0, lastSlash);
        var secondLastSlash = parentPath.LastIndexOf('/');
        var parentDirName = secondLastSlash >= 0 ? parentPath.Substring(secondLastSlash + 1) : parentPath;

        // Case 1: Texture name matches containing directory name (e.g. BG_FF4_01 inside BG_FF4_01/BgPrefab.prefab)
        // Map it directly to [GrandparentPath]/[TextureName] (e.g. Assets/.../Background/BG_FF4_01)
        if (!string.IsNullOrEmpty(parentDirName) && string.Equals(name, parentDirName, StringComparison.OrdinalIgnoreCase))
        {
            var grandparentSlash = parentPath.LastIndexOf('/');
            if (grandparentSlash >= 0)
            {
                var grandparentPath = parentPath.Substring(0, grandparentSlash);
                return $"{grandparentPath}/{name}";
            }
        }

        // Case 2: Generic prefab filename (e.g. "BgPrefab"). Omit the generic segment to avoid redundant nesting.
        if (string.Equals(prefabFileName, "BgPrefab", StringComparison.OrdinalIgnoreCase))
        {
            return $"{parentPath}/{name}";
        }

        // Case 3: Standard composite path key
        return $"{parentPath}/{prefabFileName}/{name}";
    }

    private static void ScanComponentProperties(Component comp, string addressName)
    {
        try
        {
            var type = comp.GetType();
            // Only reflect on non-Unity assemblies to keep it fast
            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var p in props)
            {
                try
                {
                    if (!p.CanRead) continue;
                    if (p.GetIndexParameters().Length > 0) continue;

                    var val = p.GetValue(comp, null);
                    if (val != null)
                    {
                        if (val is Sprite sprite)
                        {
                            RegisterSprite(sprite, addressName);
                        }
                        else if (val is Texture texture)
                        {
                            RegisterTexture(texture, addressName);
                        }
                        else if (val is Il2CppSystem.Object il2CppObj)
                        {
                            var s = il2CppObj.TryCast<Sprite>();
                            if (s != null)
                            {
                                RegisterSprite(s, addressName);
                            }
                            else
                            {
                                var t = il2CppObj.TryCast<Texture>();
                                if (t != null)
                                {
                                    RegisterTexture(t, addressName);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore property read errors
                }
            }
        }
        catch
        {
            // Ignore type reflection errors
        }
    }
}
