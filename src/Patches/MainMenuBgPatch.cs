using System;
using HarmonyLib;
using KupoUI.PR.Textures;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Creates a customizable main menu background image (<c>MainMenuBg</c>) as a sibling of the
/// <c>menu_parent</c> object and behind it, when a texture named <c>MainMenuBg</c>
/// is present in any mod folder under <c>Modules/00-Mods/</c>.
/// </summary>
[HarmonyPatch]
internal static class MainMenuBgPatch
{
    private const string TextureName = "MainMenuBg";
    private const string BgObjectName = "MainMenuBgObject";
    private const string MenuParentPath = "Canvas/aspect_parent/menu_parent";
    private const string MenuBaseClonePath = "Canvas/aspect_parent/menu_parent/menu_base(Clone)";

    private static Transform _menuParentRef;
    private static GameObject _bgObjectRef;

    // ── GameObject.SetActive ────────────────────────────────────────────────
    [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (__instance == null) return;

        // Perform active state sync and cleanup.
        if (_bgObjectRef != null)
        {
            if (_menuParentRef == null)
            {
                UnityEngine.Object.Destroy(_bgObjectRef);
                _bgObjectRef = null;
            }
            else
            {
                bool shouldBeActive = _menuParentRef.gameObject.activeSelf;
                var menuBase = _menuParentRef.Find("menu_base(Clone)");
                if (menuBase != null)
                {
                    shouldBeActive = shouldBeActive && menuBase.gameObject.activeSelf;
                }

                if (_bgObjectRef.activeSelf != shouldBeActive)
                {
                    _bgObjectRef.SetActive(shouldBeActive);
                }
            }
        }

        if (__instance.name == "menu_parent" && MatchesHierarchyPath(__instance, MenuParentPath))
        {
            _menuParentRef = __instance.transform;
            TryCreateMainMenuBg(_menuParentRef);
        }
        else if (__instance.name == "menu_base(Clone)" && MatchesHierarchyPath(__instance, MenuBaseClonePath))
        {
            _menuParentRef = __instance.transform.parent;
            TryCreateMainMenuBg(_menuParentRef);
        }
    }

    // ── SceneManager.Internal_SceneLoaded ───────────────────────────────────
    [HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
    [HarmonyPostfix]
    private static void SceneLoadedPostfix(Scene scene, LoadSceneMode mode)
    {
        if (_bgObjectRef != null)
        {
            UnityEngine.Object.Destroy(_bgObjectRef);
            _bgObjectRef = null;
        }
        _menuParentRef = null;

        var rootObjects = scene.GetRootGameObjects();
        if (rootObjects == null) return;

        foreach (var root in rootObjects)
        {
            if (root == null) continue;
            var menuParent = FindMenuParentInHierarchy(root.transform);
            if (menuParent != null)
            {
                TryCreateMainMenuBg(menuParent);
            }
        }
    }

    // ── Core creation logic ───────────────────────────────────────────────────
    private static void TryCreateMainMenuBg(Transform menuParent)
    {
        if (menuParent == null) return;

        var aspectParent = menuParent.parent;
        if (aspectParent == null) return;

        // Already injected — nothing to do.
        if (aspectParent.Find(BgObjectName) != null)
        {
            return;
        }

        // Bail out early when no texture is registered.
        if (!TextureResolver.HasTextureOverride(TextureName))
        {
            return;
        }

        // Load the texture.
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.name = TextureName;

        if (!TextureResolver.TryReplaceTextureInPlace(tex, TextureName))
        {
            KupoUIPRPlugin.PluginLog.LogWarning(
                $"[MainMenuBgPatch] Texture '{TextureName}' is indexed but could not be loaded.");
            return;
        }

        UnityEngine.Object.DontDestroyOnLoad(tex);

        // Create the background GameObject.
        var go = new GameObject(BgObjectName);
        go.transform.SetParent(aspectParent, false);

        // Place immediately before menu_parent in sibling order so it renders behind it.
        go.transform.SetSiblingIndex(menuParent.GetSiblingIndex());

        // Setup RectTransform to stretch to fill the parent.
        var rt = go.GetComponent<RectTransform>();
        if (rt == null)
        {
            rt = go.AddComponent<RectTransform>();
        }
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Add RawImage component.
        var rawImage = go.AddComponent<RawImage>();
        rawImage.texture = tex;
        rawImage.color = Color.white;

        if (tex.wrapMode == TextureWrapMode.Repeat)
        {
            var parentRt = aspectParent.GetComponent<RectTransform>();
            if (parentRt != null)
            {
                var parentRect = parentRt.rect;
                float uScale = parentRect.width / tex.width;
                float vScale = parentRect.height / tex.height;
                rawImage.uvRect = new Rect(0, 0, uScale, vScale);
            }
        }

        _bgObjectRef = go;

        // Calculate and apply initial active state.
        bool shouldBeActive = menuParent.gameObject.activeSelf;
        var menuBase = menuParent.Find("menu_base(Clone)");
        if (menuBase != null)
        {
            shouldBeActive = shouldBeActive && menuBase.gameObject.activeSelf;
        }
        go.SetActive(shouldBeActive);

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[MainMenuBgPatch] Injected '{BgObjectName}' ({tex.width}x{tex.height}) " +
            $"behind '{menuParent.name}' under '{aspectParent.name}'.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static Transform FindMenuParentInHierarchy(Transform parent)
    {
        if (parent == null) return null;

        if (parent.name == "menu_parent" && MatchesHierarchyPath(parent.gameObject, MenuParentPath))
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindMenuParentInHierarchy(parent.GetChild(i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool MatchesHierarchyPath(GameObject target, string expectedPath)
    {
        var parts = expectedPath.Split('/');
        var current = target.transform;

        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (current == null || current.name != parts[i])
            {
                return false;
            }
            current = current.parent;
        }

        return true;
    }
}
