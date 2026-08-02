using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KupoUI.PR.Compatibility;
using KupoUI.PR.ObjectConfig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Applies data-driven GameObject transform overrides defined in <c>ObjectConfig.json</c>
/// files found recursively under <c>Modules/00-Mods/</c>.
/// </summary>
[HarmonyPatch]
internal static class ObjectConfigPatch
{
    private static string _modulesRootPath;
    private static bool _hasTextColorWhiteRules;
    private static bool _hasColorRules;
    private static bool _hasDisableShadowRules;
    private static bool _hasDisableMaskRules;
    private static bool _isApplyingColor;
    private static bool _isProcessingSetActive;

    private static readonly ConditionalWeakTable<GameObject, object> _processedObjects = new();
    private static readonly ConditionalWeakTable<GameObject, object> _hierarchyProcessedObjects = new();
    private static readonly Dictionary<string, List<ObjectConfigEntry>> _entriesByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Sprite> _customImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _dummyValue = new();

    /// <summary>
    /// Called once from <see cref="KupoUIPRPlugin.Load"/> to bootstrap the system.
    /// </summary>
    internal static void Initialize(string modulesRootPath)
    {
        _modulesRootPath = modulesRootPath;
        ObjectConfigLoader.Load(_modulesRootPath);

        _hasTextColorWhiteRules = false;
        _hasColorRules = false;
        _hasDisableShadowRules = false;
        _hasDisableMaskRules = false;

        _entriesByName.Clear();
        var entries = ObjectConfigLoader.Entries;
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.TargetObjectName)) continue;
            var key = e.TargetObjectName.Trim();
            if (!_entriesByName.TryGetValue(key, out var list))
            {
                list = new List<ObjectConfigEntry>();
                _entriesByName[key] = list;
            }
            list.Add(e);

            if (e.TextColorWhite == true)
            {
                _hasTextColorWhiteRules = true;
            }
            if (e.Color.HasValue)
            {
                _hasColorRules = true;
            }
            if (e.DisableShadow == true)
            {
                _hasDisableShadowRules = true;
            }
            if (e.DisableMask == true)
            {
                _hasDisableMaskRules = true;
            }
        }

        // Log a summary of every loaded rule so the user can verify parsing in the BepInEx log.
        if (entries.Count > 0)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[ObjectConfig] {entries.Count} rule(s) ready:");
            foreach (var e in entries)
            {
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[ObjectConfig]   name='{e.TargetObjectName}'"
                    + (string.IsNullOrEmpty(e.SceneName)     ? "" : $" scene='{e.SceneName}'")
                    + (string.IsNullOrEmpty(e.TargetPath)    ? "" : $" path='{e.TargetPath}'")
                    + (e.Position.HasValue  ? $" pos=({e.Position.Value.X},{e.Position.Value.Y},{e.Position.Value.Z})"    : "")
                    + (e.Rotation.HasValue  ? $" rot=({e.Rotation.Value.X},{e.Rotation.Value.Y},{e.Rotation.Value.Z})"    : "")
                    + (e.Scale.HasValue     ? $" scale=({e.Scale.Value.X},{e.Scale.Value.Y},{e.Scale.Value.Z})"           : "")
                    + (e.Size.HasValue      ? $" size=({e.Size.Value.X},{e.Size.Value.Y})"                               : "")
                    + (e.SetActive.HasValue              ? $" setActive={e.SetActive.Value}"             : "")
                    + (string.IsNullOrEmpty(e.TextAlignment) ? "" : $" textAlignment={e.TextAlignment}")
                    + (string.IsNullOrEmpty(e.ChildAlignment) ? "" : $" childAlignment={e.ChildAlignment}")
                    + (e.TextColorWhite.HasValue             ? $" textColorWhite={e.TextColorWhite.Value}"   : "")
                    + (e.Color.HasValue                      ? $" color=#{FormatColorToHex(e.Color.Value)}" : "")
                    + (e.DisableShadow.HasValue              ? $" disableShadow={e.DisableShadow.Value}"     : "")
                    + (e.DisableMask.HasValue                ? $" disableMask={e.DisableMask.Value}"         : "")
                    + (e.IgnoreLayout.HasValue                ? $" ignoreLayout={e.IgnoreLayout.Value}"       : ""));
            }
        }

        KupoUIPRPlugin.PluginLog.LogInfo("[ObjectConfig] Patch initialized.");
    }

    private static string FormatColorToHex(Color color)
    {
        byte r = (byte)Mathf.Clamp((int)(color.r * 255f + 0.5f), 0, 255);
        byte g = (byte)Mathf.Clamp((int)(color.g * 255f + 0.5f), 0, 255);
        byte b = (byte)Mathf.Clamp((int)(color.b * 255f + 0.5f), 0, 255);
        byte a = (byte)Mathf.Clamp((int)(color.a * 255f + 0.5f), 0, 255);
        return $"{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    // -------------------------------------------------------------------------
    // Harmony hook 1 — PREFIX: blocks SetActive(true) on SetActive:false targets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Intercepts every <c>SetActive(true)</c> call. If the target GameObject
    /// matches a <c>SetActive: false</c> rule the parameter is redirected to
    /// <c>false</c> <em>before</em> Unity processes it, so the object is never
    /// actually activated regardless of what game code requests.
    /// </summary>
    [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
    [HarmonyPrefix]
    private static void SetActivePrefix(GameObject __instance, ref bool value)
    {
        // Only intercept activation attempts.
        if (!value || __instance == null) return;

        if (_entriesByName.Count == 0) return;

        var name = __instance.name;
        if (name == null) return;

        var sceneName = SceneManager.GetActiveScene().name;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                if (CheckAndApplyActiveRule(__instance, entry, sceneName, ref value))
                {
                    return;
                }
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                if (CheckAndApplyActiveRule(__instance, entry, sceneName, ref value))
                {
                    return;
                }
            }
        }
    }

    private static bool CheckAndApplyActiveRule(GameObject go, ObjectConfigEntry entry, string sceneName, ref bool value)
    {
        // Only care about rules that want the object kept inactive.
        if (!entry.SetActive.HasValue || entry.SetActive.Value) return false;

        if (!string.IsNullOrEmpty(entry.SceneName)
            && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(entry.TargetPath)
            && !MatchesHierarchyPath(go, entry.TargetPath))
        {
            return false;
        }

        // Redirect: the original SetActive will receive false instead of true.
        value = false;
        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[ObjectConfig] Blocked SetActive(true) on '{go.name}' — rule keeps it inactive.");
        return true;
    }

    // -------------------------------------------------------------------------
    // Harmony hook 2 — POSTFIX: hierarchy scan when a parent is activated
    // -------------------------------------------------------------------------

    /// <summary>
    /// When a parent has <c>SetActive(true)</c> called, Unity propagates the
    /// active state to all children internally — without calling <c>SetActive</c>
    /// on each child. This postfix scans the full hierarchy after any activation
    /// so that matching children are explicitly disabled.
    /// </summary>
    [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (!value || __instance == null || _isProcessingSetActive)
        {
            return;
        }

        // If the hierarchy has already been fully processed, skip it completely.
        if (_hierarchyProcessedObjects.TryGetValue(__instance, out _))
        {
            return;
        }

        _isProcessingSetActive = true;
        try
        {
            var sceneName = SceneManager.GetActiveScene().name;
            ApplyToHierarchy(__instance, sceneName);
        }
        finally
        {
            _isProcessingSetActive = false;
        }
    }

    // -------------------------------------------------------------------------
    // Harmony hook 2 — fires when Unity finishes loading a scene.
    // Patching the internal method avoids IL2CPP UnityAction delegate issues.
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
    [HarmonyPostfix]
    private static void SceneLoadedPostfix(Scene scene, LoadSceneMode mode)
    {
        ExternalModDetector.LogLoadedOptionalMods(KupoUIPRPlugin.PluginLog);
        var sceneName = scene.name;
        KupoUIPRPlugin.PluginLog.LogInfo($"[ObjectConfig] Scene loaded: '{sceneName}' (mode={mode}). Scanning hierarchy...");

        var rootObjects = scene.GetRootGameObjects();
        KupoUIPRPlugin.PluginLog.LogInfo($"[ObjectConfig] {rootObjects.Length} root object(s) in scene '{sceneName}'.");

        foreach (var root in rootObjects)
        {
            ApplyToHierarchy(root, sceneName);
        }
    }

    // -------------------------------------------------------------------------
    // Hierarchy traversal
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks the entire transform hierarchy (including inactive objects) and
    /// applies all matching rules.
    /// </summary>
    private static void ApplyToHierarchy(GameObject go, string sceneName)
    {
        if (go == null)
        {
            return;
        }

        // Check if hierarchy processing was already done for this root object.
        if (_hierarchyProcessedObjects.TryGetValue(go, out _))
        {
            return;
        }

        var allTransforms = go.GetComponentsInChildren<Transform>(includeInactive: true);
        foreach (var t in allTransforms)
        {
            if (t != null && t.gameObject != null)
            {
                var targetGo = t.gameObject;
                if (!_processedObjects.TryGetValue(targetGo, out _))
                {
                    ApplyMatchingRules(targetGo, sceneName);
                    _processedObjects.Add(targetGo, _dummyValue);
                }
            }
        }

        _hierarchyProcessedObjects.Add(go, _dummyValue);
    }

    // -------------------------------------------------------------------------
    // Core matching and application logic
    // -------------------------------------------------------------------------

    private static void ApplyMatchingRules(GameObject go, string currentScene)
    {
        if (go == null)
        {
            return;
        }

        if (_entriesByName.Count == 0)
        {
            return;
        }

        var name = go.name;
        if (name == null) return;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                CheckAndApplyRule(go, entry, currentScene);
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                CheckAndApplyRule(go, entry, currentScene);
            }
        }
    }

    private static bool CheckAndApplyRule(GameObject go, ObjectConfigEntry entry, string currentScene)
    {
        if (!string.IsNullOrEmpty(entry.SceneName)
            && !entry.SceneName.Equals(currentScene, StringComparison.OrdinalIgnoreCase))
        {
            KupoUIPRPlugin.PluginLog.LogDebug(
                $"[ObjectConfig] Name match '{go.name}' — scene MISMATCH: config='{entry.SceneName}' actual='{currentScene}'");
            return false;
        }

        if (!string.IsNullOrEmpty(entry.TargetPath)
            && !MatchesHierarchyPath(go, entry.TargetPath))
        {
            KupoUIPRPlugin.PluginLog.LogDebug(
                $"[ObjectConfig] Name match '{go.name}' — path MISMATCH: expected='{entry.TargetPath}' actual='{BuildTransformPath(go)}'");
            return false;
        }

        ApplyEntry(go, entry);
        return true;
    }

    private static void ApplyEntry(GameObject go, ObjectConfigEntry entry)
    {
        var t = go.transform;

        if (entry.Position.HasValue)
        {
            var p = entry.Position.Value;
            var targetPos = new Vector3(p.X, p.Y, p.Z);
            if (Vector3.SqrMagnitude(t.localPosition - targetPos) > 1e-6f)
            {
                t.localPosition = targetPos;
            }
        }

        if (entry.Rotation.HasValue)
        {
            var r = entry.Rotation.Value;
            var targetRot = new Vector3(r.X, r.Y, r.Z);
            if (Vector3.SqrMagnitude(t.localEulerAngles - targetRot) > 1e-6f)
            {
                t.localEulerAngles = targetRot;
            }
        }

        if (entry.Scale.HasValue)
        {
            var s = entry.Scale.Value;
            var targetScale = new Vector3(s.X, s.Y, s.Z);
            if (Vector3.SqrMagnitude(t.localScale - targetScale) > 1e-6f)
            {
                t.localScale = targetScale;
            }
        }

        if (entry.Size.HasValue)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                var s = entry.Size.Value;
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, s.X);
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, s.Y);
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] Size specified for '{go.name}' but no RectTransform component found.");
            }
        }

        if (entry.SetActive.HasValue)
        {
            if (go.activeSelf != entry.SetActive.Value)
            {
                go.SetActive(entry.SetActive.Value);
            }
        }

        if (!string.IsNullOrEmpty(entry.TextAlignment))
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (System.Enum.TryParse(entry.TextAlignment, ignoreCase: true, out TextAnchor anchor))
                {
                    if (textComp.alignment != anchor)
                    {
                        textComp.alignment = anchor;
                    }
                }
                else
                {
                    KupoUIPRPlugin.PluginLog.LogWarning(
                        $"[ObjectConfig] Unknown TextAlignment '{entry.TextAlignment}' on '{go.name}'. "
                        + "Valid values: UpperLeft, UpperCenter, UpperRight, "
                        + "MiddleLeft, MiddleCenter, MiddleRight, "
                        + "LowerLeft, LowerCenter, LowerRight.");
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] TextAlignment specified for '{go.name}' but no Text component found.");
            }
        }

        if (!string.IsNullOrEmpty(entry.ChildAlignment))
        {
            var layoutComp = go.GetComponent<LayoutGroup>();
            if (layoutComp != null)
            {
                if (System.Enum.TryParse(entry.ChildAlignment, ignoreCase: true, out TextAnchor anchor))
                {
                    if (layoutComp.childAlignment != anchor)
                    {
                        layoutComp.childAlignment = anchor;
                    }
                }
                else
                {
                    KupoUIPRPlugin.PluginLog.LogWarning(
                        $"[ObjectConfig] Unknown ChildAlignment '{entry.ChildAlignment}' on '{go.name}'. "
                        + "Valid values: UpperLeft, UpperCenter, UpperRight, "
                        + "MiddleLeft, MiddleCenter, MiddleRight, "
                        + "LowerLeft, LowerCenter, LowerRight.");
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] ChildAlignment specified for '{go.name}' but no LayoutGroup component found.");
            }
        }

        if (entry.FontSize.HasValue)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (textComp.fontSize != entry.FontSize.Value)
                {
                    textComp.fontSize = entry.FontSize.Value;
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] FontSize specified for '{go.name}' but no Text component found.");
            }
        }

        if (entry.ResizeTextForBestFit.HasValue)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (textComp.resizeTextForBestFit != entry.ResizeTextForBestFit.Value)
                {
                    textComp.resizeTextForBestFit = entry.ResizeTextForBestFit.Value;
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] ResizeTextForBestFit specified for '{go.name}' but no Text component found.");
            }
        }

        if (entry.ResizeTextMaxSize.HasValue)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (textComp.resizeTextMaxSize != entry.ResizeTextMaxSize.Value)
                {
                    textComp.resizeTextMaxSize = entry.ResizeTextMaxSize.Value;
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] ResizeTextMaxSize specified for '{go.name}' but no Text component found.");
            }
        }

        if (entry.ResizeTextMinSize.HasValue)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (textComp.resizeTextMinSize != entry.ResizeTextMinSize.Value)
                {
                    textComp.resizeTextMinSize = entry.ResizeTextMinSize.Value;
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] ResizeTextMinSize specified for '{go.name}' but no Text component found.");
            }
        }

        if (entry.TextColorWhite.HasValue && entry.TextColorWhite.Value && !entry.Color.HasValue)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                if (textComp.color != Color.white)
                {
                    EnforceGraphicColor(textComp, Color.white);
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] TextColorWhite specified for '{go.name}' but no Text component found.");
            }
        }

        if (entry.Color.HasValue)
        {
            var graphics = go.GetComponents<Graphic>();
            if (graphics == null || graphics.Length == 0)
            {
                graphics = go.GetComponentsInChildren<Graphic>(true);
            }

            if (graphics != null && graphics.Length > 0)
            {
                foreach (var g in graphics)
                {
                    if (g != null && g.color != entry.Color.Value)
                    {
                        EnforceGraphicColor(g, entry.Color.Value);
                    }
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] Color specified for '{go.name}' but no Graphic component found.");
            }
        }

        if (entry.DisableShadow.HasValue && entry.DisableShadow.Value)
        {
            var shadows = go.GetComponents<Shadow>();
            if (shadows.Length > 0)
            {
                foreach (var shadow in shadows)
                {
                    if (shadow != null && shadow.enabled)
                    {
                        shadow.enabled = false;
                    }
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning(
                    $"[ObjectConfig] DisableShadow specified for '{go.name}' but no Shadow component found.");
            }
        }

        if (entry.DisableMask.HasValue && entry.DisableMask.Value)
        {
            var masks = go.GetComponents<Mask>();
            foreach (var mask in masks)
            {
                if (mask != null && mask.enabled)
                {
                    mask.enabled = false;
                }
            }
            var rectMasks = go.GetComponents<RectMask2D>();
            foreach (var rectMask in rectMasks)
            {
                if (rectMask != null && rectMask.enabled)
                {
                    rectMask.enabled = false;
                }
            }
        }

        if (entry.NewImages != null && entry.NewImages.Count > 0)
        {
            foreach (var imgConfig in entry.NewImages)
            {
                var sprite = GetOrCreateCustomSprite(imgConfig.ImagePath, entry.SourceFile);
                if (sprite == null)
                {
                    continue;
                }

                var childTransform = go.transform.Find(imgConfig.Name);
                GameObject childGo;
                RectTransform rectTransform;
                Image imageComponent;

                if (childTransform != null)
                {
                    childGo = childTransform.gameObject;
                    rectTransform = childGo.GetComponent<RectTransform>();
                    imageComponent = childGo.GetComponent<Image>();
                }
                else
                {
                    childGo = new GameObject(imgConfig.Name);
                    childGo.transform.SetParent(go.transform, false);

                    rectTransform = childGo.AddComponent<RectTransform>();
                    imageComponent = childGo.AddComponent<Image>();
                }

                if (imageComponent != null)
                {
                    imageComponent.sprite = sprite;
                    if (!string.IsNullOrEmpty(imgConfig.ImageType))
                    {
                        if (System.Enum.TryParse(imgConfig.ImageType, ignoreCase: true, out Image.Type parsedType))
                        {
                            imageComponent.type = parsedType;
                        }
                        else
                        {
                            KupoUIPRPlugin.PluginLog.LogWarning(
                                $"[ObjectConfig] Invalid ImageType '{imgConfig.ImageType}' specified for child '{imgConfig.Name}'. Use Simple, Sliced, Tiled, or Filled.");
                        }
                    }
                    else if (sprite != null && sprite.border != Vector4.zero)
                    {
                        imageComponent.type = Image.Type.Sliced;
                    }
                }

                if (imgConfig.Position.HasValue)
                {
                    var p = imgConfig.Position.Value;
                    rectTransform.localPosition = new Vector3(p.X, p.Y, p.Z);
                }

                if (imgConfig.Rotation.HasValue)
                {
                    var r = imgConfig.Rotation.Value;
                    rectTransform.localEulerAngles = new Vector3(r.X, r.Y, r.Z);
                }

                if (imgConfig.Scale.HasValue)
                {
                    var s = imgConfig.Scale.Value;
                    rectTransform.localScale = new Vector3(s.X, s.Y, s.Z);
                }
                else if (childTransform == null)
                {
                    rectTransform.localScale = Vector3.one;
                }

                if (imgConfig.Size.HasValue)
                {
                    var sz = imgConfig.Size.Value;
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sz.X);
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sz.Y);
                }
                else if (childTransform == null)
                {
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sprite.rect.width);
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sprite.rect.height);
                }

                if (imgConfig.Color.HasValue && imageComponent != null)
                {
                    imageComponent.color = imgConfig.Color.Value;
                }
            }
        }

        if (entry.IgnoreLayout.HasValue && entry.IgnoreLayout.Value)
        {
            var layoutElement = go.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = go.AddComponent<LayoutElement>();
            }
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }
        }

        KupoUIPRPlugin.PluginLog.LogDebug(
            $"[ObjectConfig] Applied rule to '{go.name}' (from {System.IO.Path.GetFileName(entry.SourceFile)})");
    }

    // -------------------------------------------------------------------------
    // TextColorWhite enforcement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Intercepts every <c>Graphic.color</c> write. If the target has a matching
    /// <c>TextColorWhite: true</c> rule the color value is overridden to white
    /// before Unity applies it, preventing the game from resetting it later.
    /// </summary>
    [HarmonyPatch(typeof(Graphic), nameof(Graphic.color), MethodType.Setter)]
    [HarmonyPrefix]
    private static void GraphicColorSetterPrefix(Graphic __instance, ref Color value)
    {
        if ((!_hasTextColorWhiteRules && !_hasColorRules) || _isApplyingColor) return;
        if (__instance == null) return;

        var name = __instance.name;
        if (name == null) return;

        var sceneName = SceneManager.GetActiveScene().name;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                if (ApplyColorRule(__instance, entry, sceneName, ref value)) return;
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                if (ApplyColorRule(__instance, entry, sceneName, ref value)) return;
            }
        }
    }

    private static bool ApplyColorRule(Graphic graphic, ObjectConfigEntry entry, string sceneName, ref Color value)
    {
        if (entry.TextColorWhite != true && !entry.Color.HasValue) return false;
        if (!string.IsNullOrEmpty(entry.SceneName)
            && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(entry.TargetPath)
            && !MatchesHierarchyPath(graphic.gameObject, entry.TargetPath)) return false;

        if (entry.Color.HasValue)
        {
            value = entry.Color.Value;
        }
        else if (entry.TextColorWhite == true)
        {
            value = Color.white;
        }
        return true;
    }

    /// <summary>
    /// Intercepts BaseMeshEffect.OnEnable. If the target has a matching DisableShadow rule,
    /// we force it to remain disabled immediately.
    /// </summary>
    [HarmonyPatch(typeof(BaseMeshEffect), "OnEnable")]
    [HarmonyPostfix]
    private static void BaseMeshEffectOnEnablePostfix(BaseMeshEffect __instance)
    {
        if (!_hasDisableShadowRules) return;
        if (__instance == null || __instance.gameObject == null) return;
        if (!(__instance is Shadow)) return;

        var name = __instance.name;
        if (name == null) return;

        var sceneName = SceneManager.GetActiveScene().name;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableShadowRule(__instance, entry, sceneName)) return;
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableShadowRule(__instance, entry, sceneName)) return;
            }
        }
    }

    private static bool ApplyDisableShadowRule(BaseMeshEffect instance, ObjectConfigEntry entry, string sceneName)
    {
        if (entry.DisableShadow != true) return false;
        if (!string.IsNullOrEmpty(entry.SceneName)
            && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(entry.TargetPath)
            && !MatchesHierarchyPath(instance.gameObject, entry.TargetPath)) return false;

        instance.enabled = false;
        return true;
    }

    /// <summary>
    /// Intercepts Mask.OnEnable. If the target has a matching DisableMask rule,
    /// we force it to remain disabled immediately.
    /// </summary>
    [HarmonyPatch(typeof(Mask), "OnEnable")]
    [HarmonyPostfix]
    private static void MaskOnEnablePostfix(Mask __instance)
    {
        if (!_hasDisableMaskRules) return;
        if (__instance == null || __instance.gameObject == null) return;

        var name = __instance.name;
        if (name == null) return;

        var sceneName = SceneManager.GetActiveScene().name;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableMaskRule(__instance.gameObject, entry, sceneName)) return;
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableMaskRule(__instance.gameObject, entry, sceneName)) return;
            }
        }
    }

    /// <summary>
    /// Intercepts RectMask2D.OnEnable. If the target has a matching DisableMask rule,
    /// we force it to remain disabled immediately.
    /// </summary>
    [HarmonyPatch(typeof(RectMask2D), "OnEnable")]
    [HarmonyPostfix]
    private static void RectMask2DOnEnablePostfix(RectMask2D __instance)
    {
        if (!_hasDisableMaskRules) return;
        if (__instance == null || __instance.gameObject == null) return;

        var name = __instance.name;
        if (name == null) return;

        var sceneName = SceneManager.GetActiveScene().name;

        if (_entriesByName.TryGetValue(name, out var list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableMaskRule(__instance.gameObject, entry, sceneName)) return;
            }
        }

        var trimmed = name.Trim();
        if (trimmed != name && _entriesByName.TryGetValue(trimmed, out list))
        {
            foreach (var entry in list)
            {
                if (ApplyDisableMaskRule(__instance.gameObject, entry, sceneName)) return;
            }
        }
    }

    private static bool ApplyDisableMaskRule(GameObject go, ObjectConfigEntry entry, string sceneName)
    {
        if (entry.DisableMask != true) return false;
        if (!string.IsNullOrEmpty(entry.SceneName)
            && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(entry.TargetPath)
            && !MatchesHierarchyPath(go, entry.TargetPath)) return false;

        var masks = go.GetComponents<Mask>();
        foreach (var mask in masks)
        {
            if (mask != null && mask.enabled) mask.enabled = false;
        }
        var rectMasks = go.GetComponents<RectMask2D>();
        foreach (var rectMask in rectMasks)
        {
            if (rectMask != null && rectMask.enabled) rectMask.enabled = false;
        }
        return true;
    }

    /// <summary>
    /// Sets the color of a <see cref="Graphic"/> component to the target color, guarded by
    /// a re-entrancy flag to prevent infinite loops when Harmony intercepts the setter.
    /// </summary>
    private static void EnforceGraphicColor(Graphic graphicComp, Color color)
    {
        if (_isApplyingColor || graphicComp == null) return;
        _isApplyingColor = true;
        try
        {
            graphicComp.color = color;
        }
        finally
        {
            _isApplyingColor = false;
        }
    }

    // -------------------------------------------------------------------------
    // Hierarchy path matching (mirrors ScaledDownMenuPatch logic)
    // -------------------------------------------------------------------------

    private static bool IsNameMatch(string name1, string name2)
    {
        if (name1 == null || name2 == null) return false;
        if (string.Equals(name1, name2, StringComparison.Ordinal)) return true;
        return string.Equals(name1.Trim(), name2.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up the transform hierarchy and verifies that the path from some
    /// ancestor down to <paramref name="target"/> matches <paramref name="expectedPath"/>.
    /// Path uses forward-slash notation and is matched from the target upward.
    /// Supports index targeting, e.g. "Text[1]" matches the second child named "Text".
    /// </summary>
    private static bool MatchesHierarchyPath(GameObject target, string expectedPath)
    {
        var parts = expectedPath.Split('/');

        var current = target.transform;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (current == null)
            {
                return false;
            }

            var segment = parts[i];
            var expectedName = segment;
            int? expectedIndex = null;

            if (segment.EndsWith("]") && segment.Contains("["))
            {
                var openBrace = segment.LastIndexOf('[');
                var closeBrace = segment.LastIndexOf(']');
                if (openBrace >= 0 && closeBrace > openBrace)
                {
                    var indexStr = segment.Substring(openBrace + 1, closeBrace - openBrace - 1);
                    if (int.TryParse(indexStr, out var parsedIndex))
                    {
                        expectedName = segment.Substring(0, openBrace);
                        expectedIndex = parsedIndex;
                    }
                }
            }

            if (!IsNameMatch(current.name, expectedName))
            {
                return false;
            }

            if (expectedIndex.HasValue)
            {
                var actualIndex = GetNameSiblingIndex(current, expectedName);
                if (actualIndex != expectedIndex.Value)
                {
                    return false;
                }
            }

            current = current.parent;
        }

        return true;
    }

    private static int GetNameSiblingIndex(Transform transform, string expectedName)
    {
        var parent = transform.parent;
        if (parent != null)
        {
            int index = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == transform)
                {
                    return index;
                }
                if (IsNameMatch(child.name, expectedName))
                {
                    index++;
                }
            }
        }
        else
        {
            var scene = transform.gameObject.scene;
            if (scene.IsValid())
            {
                var rootObjects = scene.GetRootGameObjects();
                int index = 0;
                foreach (var root in rootObjects)
                {
                    if (root.transform == transform)
                    {
                        return index;
                    }
                    if (IsNameMatch(root.name, expectedName))
                    {
                        index++;
                    }
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Builds the full transform path for a GameObject (used in diagnostic mismatch logs).
    /// </summary>
    private static string BuildTransformPath(GameObject go)
    {
        if (go == null)
        {
            return string.Empty;
        }

        var path = go.name;
        var current = go.transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static Sprite GetOrCreateCustomSprite(string imagePath, string entrySourceFile)
    {
        var isSimpleName = !imagePath.Contains("/") && !imagePath.Contains("\\");
        var cleanName = imagePath;
        if (isSimpleName)
        {
            if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                imagePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                imagePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                imagePath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                imagePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = System.IO.Path.GetFileNameWithoutExtension(imagePath);
            }
        }

        if (isSimpleName)
        {
            var cacheKey = "Resolver::" + cleanName;
            if (_customImageCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            KupoUI.PR.Textures.TextureResolver.TextureOverrideMetadata metadata;
            var tex = KupoUI.PR.Textures.TextureResolver.LoadTexture(cleanName, null, out metadata);
            if (tex != null)
            {
                var rect = new Rect(0, 0, tex.width, tex.height);
                if (metadata != null)
                {
                    var rx = metadata.RectX ?? 0;
                    var ry = metadata.RectY ?? 0;
                    var rw = metadata.Width > 0 ? metadata.Width : tex.width;
                    var rh = metadata.Height > 0 ? metadata.Height : tex.height;
                    rect = new Rect(rx, ry, rw, rh);
                }

                var pivot = new Vector2(0.5f, 0.5f);
                if (metadata != null)
                {
                    var metadataPivot = KupoUI.PR.Textures.TextureResolver.ParsePivot(metadata);
                    if (metadataPivot.HasValue)
                    {
                        pivot = metadataPivot.Value;
                    }
                }

                var border = Vector4.zero;
                if (metadata != null)
                {
                    var metadataBorder = KupoUI.PR.Textures.TextureResolver.ParseBorder(metadata);
                    if (metadataBorder.HasValue)
                    {
                        border = metadataBorder.Value;
                    }
                }

                var pixelsPerUnit = 100f;
                if (metadata != null && metadata.PixelsPerUnit > 0f)
                {
                    pixelsPerUnit = metadata.PixelsPerUnit;
                }

                var sprite = Sprite.Create(
                    tex,
                    rect,
                    pivot,
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect,
                    border);

                sprite.name = cleanName + "_Custom";
                UnityEngine.Object.DontDestroyOnLoad(sprite);

                _customImageCache[cacheKey] = sprite;
                return sprite;
            }

            // Fallback to searching the base game sprites loaded in memory
            var baseGameSprite = FindBaseGameSprite(cleanName);
            if (baseGameSprite != null)
            {
                _customImageCache[cacheKey] = baseGameSprite;
                return baseGameSprite;
            }
        }

        var relativePath = imagePath;
        var baseDir = System.IO.Path.GetDirectoryName(entrySourceFile);
        var absolutePath = System.IO.Path.Combine(baseDir, relativePath);
        try
        {
            absolutePath = System.IO.Path.GetFullPath(absolutePath);
        }
        catch
        {
            // Fallback
        }

        if (_customImageCache.TryGetValue(absolutePath, out var cachedFile) && cachedFile != null)
        {
            return cachedFile;
        }

        try
        {
            if (System.IO.File.Exists(absolutePath))
            {
                var bytes = System.IO.File.ReadAllBytes(absolutePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(tex, bytes))
                {
                    var metadata = KupoUI.PR.Textures.TextureResolver.LoadTextureMetadata(absolutePath);
                    tex.filterMode = KupoUI.PR.Textures.TextureResolver.ResolveFilterMode(absolutePath, metadata);
                    tex.wrapMode = KupoUI.PR.Textures.TextureResolver.ResolveWrapMode(absolutePath, metadata);

                    var rect = new Rect(0, 0, tex.width, tex.height);
                    if (metadata != null)
                    {
                        var rx = metadata.RectX ?? 0;
                        var ry = metadata.RectY ?? 0;
                        var rw = metadata.Width > 0 ? metadata.Width : tex.width;
                        var rh = metadata.Height > 0 ? metadata.Height : tex.height;
                        rect = new Rect(rx, ry, rw, rh);
                    }

                    var pivot = new Vector2(0.5f, 0.5f);
                    if (metadata != null)
                    {
                        var metadataPivot = KupoUI.PR.Textures.TextureResolver.ParsePivot(metadata);
                        if (metadataPivot.HasValue)
                        {
                            pivot = metadataPivot.Value;
                        }
                    }

                    var border = Vector4.zero;
                    if (metadata != null)
                    {
                        var metadataBorder = KupoUI.PR.Textures.TextureResolver.ParseBorder(metadata);
                        if (metadataBorder.HasValue)
                        {
                            border = metadataBorder.Value;
                        }
                    }

                    var pixelsPerUnit = 100f;
                    if (metadata != null && metadata.PixelsPerUnit > 0f)
                    {
                        pixelsPerUnit = metadata.PixelsPerUnit;
                    }

                    var sprite = Sprite.Create(
                        tex,
                        rect,
                        pivot,
                        pixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect,
                        border);

                    sprite.name = System.IO.Path.GetFileNameWithoutExtension(absolutePath) + "_Custom";
                    
                    UnityEngine.Object.DontDestroyOnLoad(tex);
                    UnityEngine.Object.DontDestroyOnLoad(sprite);

                    _customImageCache[absolutePath] = sprite;
                    return sprite;
                }
            }
            else
            {
                KupoUIPRPlugin.PluginLog.LogWarning($"[ObjectConfig] Custom image file not found: {absolutePath}");
            }
        }
        catch (Exception ex)
        {
            KupoUIPRPlugin.PluginLog.LogWarning($"[ObjectConfig] Failed to load custom image '{absolutePath}': {ex.Message}");
        }

        return null;
    }

    private static Sprite FindBaseGameSprite(string spriteName)
    {
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in sprites)
        {
            if (s != null)
            {
                var cleanName = KupoUI.PR.Textures.TextureResolver.NormalizeName(s.name);
                if (string.Equals(cleanName, spriteName, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
        }
        return null;
    }
}
