using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Management;
using UnityEngine;

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
            var instance = ResourceManager.Instance;
            if (instance == null)
            {
                return;
            }

            var dic = instance.completeAssetDic;
            if (dic == null || !dic.ContainsKey(addressName))
            {
                return;
            }

            var asset = dic[addressName];
            if (asset == null)
            {
                return;
            }

            AddressByPointer[asset.Pointer] = addressName;

            // UI code can receive cloned Sprite instances later, but they usually still point
            // at the original texture. Index the texture pointer as well so path-based
            // resolution survives sprite cloning/instantiation.
            if (asset is Sprite sprite && sprite.texture != null)
            {
                AddressByPointer[sprite.texture.Pointer] = addressName;
            }
        }
        catch
        {
            // Best-effort; silently ignore any IL2CPP interop errors
        }
    }
}
