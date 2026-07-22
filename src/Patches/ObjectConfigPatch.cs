using System;
using HarmonyLib;
using KupoUI.PR.ObjectConfig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KupoUI.PR.Patches;

/// <summary>
/// Applies data-driven GameObject transform overrides defined in <c>ObjectConfig.json</c>
/// files found recursively under <c>Modules/00-Mods/</c>.
/// <para>
/// <c>SetActive: false</c> rules are enforced at three levels:
/// <list type="bullet">
///   <item><description>Prefix on <see cref="GameObject.SetActive"/> — flips any direct
///   <c>SetActive(true)</c> call on the target to <c>false</c> before Unity processes it.</description></item>
///   <item><description>Postfix on <see cref="GameObject.SetActive"/> — when a <em>parent</em> is
///   activated Unity propagates active-state to children without calling SetActive on them;
///   the postfix scans the hierarchy and explicitly disables matching children.</description></item>
///   <item><description>Postfix on <c>Internal_SceneLoaded</c> — initial sweep when a scene loads.</description></item>
/// </list>
/// </para>
/// </summary>
[HarmonyPatch]
internal static class ObjectConfigPatch
{
    private static string _modulesRootPath;
    private static bool _hasTextColorWhiteRules;
    private static bool _hasColorRules;
    private static bool _hasDisableShadowRules;
    private static bool _isApplyingColor;
    private static bool _isProcessingSetActive;

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
        var entries = ObjectConfigLoader.Entries;
        foreach (var e in entries)
        {
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
                    + (e.DisableShadow.HasValue              ? $" disableShadow={e.DisableShadow.Value}"     : ""));
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

        var entries = ObjectConfigLoader.Entries;
        if (entries.Count == 0) return;

        var sceneName = SceneManager.GetActiveScene().name;

        foreach (var entry in entries)
        {
            // Only care about rules that want the object kept inactive.
            if (!entry.SetActive.HasValue || entry.SetActive.Value) continue;

            if (!IsNameMatch(__instance.name, entry.TargetObjectName)) continue;

            if (!string.IsNullOrEmpty(entry.SceneName)
                && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(entry.TargetPath)
                && !MatchesHierarchyPath(__instance, entry.TargetPath))
            {
                continue;
            }

            // Redirect: the original SetActive will receive false instead of true.
            value = false;
            KupoUIPRPlugin.PluginLog.LogInfo(
                $"[ObjectConfig] Blocked SetActive(true) on '{__instance.name}' — rule keeps it inactive.");
            return;
        }
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
    /// <para>
    /// <see cref="Transform.GetChild"/> only iterates <em>direct</em> children
    /// and does not surface inactive objects in all Unity/IL2CPP builds.
    /// Using <c>GetComponentsInChildren&lt;Transform&gt;(true)</c> guarantees that
    /// objects with <c>SetActive: false</c> — which may already be inactive at
    /// scene-load time — are still reached.
    /// </para>
    /// </summary>
    private static void ApplyToHierarchy(GameObject go, string sceneName)
    {
        if (go == null)
        {
            return;
        }

        // includeInactive: true is critical — without it, GameObjects that are
        // already inactive are skipped entirely, so "SetActive": false rules
        // would never fire on objects that start inactive.
        var allTransforms = go.GetComponentsInChildren<Transform>(includeInactive: true);
        foreach (var t in allTransforms)
        {
            if (t != null && t.gameObject != null)
            {
                ApplyMatchingRules(t.gameObject, sceneName);
            }
        }
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

        var entries = ObjectConfigLoader.Entries;
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            // Log near-misses: name matched but scene or path did not, so the user
            // can see what the game actually reports vs. what the config expects.
            if (IsNameMatch(go.name, entry.TargetObjectName))
            {
                if (!string.IsNullOrEmpty(entry.SceneName)
                    && !entry.SceneName.Equals(currentScene, StringComparison.OrdinalIgnoreCase))
                {
                    KupoUIPRPlugin.PluginLog.LogDebug(
                        $"[ObjectConfig] Name match '{go.name}' — scene MISMATCH: config='{entry.SceneName}' actual='{currentScene}'");
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.TargetPath)
                    && !MatchesHierarchyPath(go, entry.TargetPath))
                {
                    KupoUIPRPlugin.PluginLog.LogDebug(
                        $"[ObjectConfig] Name match '{go.name}' — path MISMATCH: expected='{entry.TargetPath}' actual='{BuildTransformPath(go)}'");
                    continue;
                }

                ApplyEntry(go, entry);
            }
        }
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

        var sceneName = SceneManager.GetActiveScene().name;
        foreach (var entry in ObjectConfigLoader.Entries)
        {
            if (entry.TextColorWhite != true && !entry.Color.HasValue) continue;
            if (!IsNameMatch(__instance.name, entry.TargetObjectName)) continue;
            if (!string.IsNullOrEmpty(entry.SceneName)
                && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(entry.TargetPath)
                && !MatchesHierarchyPath(__instance.gameObject, entry.TargetPath)) continue;

            if (entry.Color.HasValue)
            {
                value = entry.Color.Value;
            }
            else if (entry.TextColorWhite == true)
            {
                value = Color.white;
            }
            return;
        }
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

        var sceneName = SceneManager.GetActiveScene().name;
        foreach (var entry in ObjectConfigLoader.Entries)
        {
            if (entry.DisableShadow != true) continue;
            if (!IsNameMatch(__instance.name, entry.TargetObjectName)) continue;
            if (!string.IsNullOrEmpty(entry.SceneName)
                && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(entry.TargetPath)
                && !MatchesHierarchyPath(__instance.gameObject, entry.TargetPath)) continue;

            __instance.enabled = false;
            return;
        }
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
    /// </summary>
    private static bool MatchesHierarchyPath(GameObject target, string expectedPath)
    {
        var parts = expectedPath.Split('/');

        var current = target.transform;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (current == null || !IsNameMatch(current.name, parts[i]))
            {
                return false;
            }

            current = current.parent;
        }

        return true;
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
}
