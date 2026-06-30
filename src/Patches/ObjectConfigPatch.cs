using System;
using HarmonyLib;
using KupoUI.PR.ObjectConfig;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KupoUI.PR.Patches;

/// <summary>
/// Applies data-driven GameObject transform overrides defined in <c>ObjectConfig.json</c>
/// files found recursively under <c>Modules/00-Mods/</c>.
/// <para>
/// Rules are applied at two points:
/// <list type="bullet">
///   <item><description>When a scene finishes loading — catches objects active from the start.</description></item>
///   <item><description>When <see cref="GameObject.SetActive"/> is called with <c>true</c> — catches objects enabled later.</description></item>
/// </list>
/// </para>
/// </summary>
[HarmonyPatch]
internal static class ObjectConfigPatch
{
    private static string _modulesRootPath;

    /// <summary>
    /// Called once from <see cref="KupoUIPRPlugin.Load"/> to bootstrap the system.
    /// </summary>
    internal static void Initialize(string modulesRootPath)
    {
        _modulesRootPath = modulesRootPath;
        ObjectConfigLoader.Load(_modulesRootPath);

        // Log a summary of every loaded rule so the user can verify parsing in the BepInEx log.
        var entries = ObjectConfigLoader.Entries;
        if (entries.Count > 0)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[ObjectConfig] {entries.Count} rule(s) ready:");
            foreach (var e in entries)
            {
                KupoUIPRPlugin.PluginLog.LogInfo(
                    $"[ObjectConfig]   name='{e.TargetObjectName}'"
                    + (string.IsNullOrEmpty(e.SceneName)  ? "" : $" scene='{e.SceneName}'")
                    + (string.IsNullOrEmpty(e.TargetPath) ? "" : $" path='{e.TargetPath}'")
                    + (e.Position.HasValue  ? $" pos=({e.Position.Value.X},{e.Position.Value.Y},{e.Position.Value.Z})"    : "")
                    + (e.Rotation.HasValue  ? $" rot=({e.Rotation.Value.X},{e.Rotation.Value.Y},{e.Rotation.Value.Z})"    : "")
                    + (e.Scale.HasValue     ? $" scale=({e.Scale.Value.X},{e.Scale.Value.Y},{e.Scale.Value.Z})"           : "")
                    + (e.SetActive.HasValue ? $" setActive={e.SetActive.Value}"                                           : ""));
            }
        }

        KupoUIPRPlugin.PluginLog.LogInfo("[ObjectConfig] Patch initialized.");
    }

    // -------------------------------------------------------------------------
    // Harmony hook 1 — fires on every GameObject.SetActive(true) call
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (!value)
        {
            return;
        }

        var sceneName = SceneManager.GetActiveScene().name;

        // Scan __instance AND its full child hierarchy.
        // This catches cases where a target object (e.g. main_menu) is already
        // active inside a parent (e.g. title(Clone)) that gets instantiated or
        // set active — Unity never calls SetActive on the children in that flow,
        // so checking only __instance would miss them.
        ApplyToHierarchy(__instance, sceneName);
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
    /// Recursively walks the transform hierarchy and applies all matching rules.
    /// </summary>
    private static void ApplyToHierarchy(GameObject go, string sceneName)
    {
        if (go == null)
        {
            return;
        }

        ApplyMatchingRules(go, sceneName);

        var transform = go.transform;
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child != null)
            {
                ApplyToHierarchy(child.gameObject, sceneName);
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
            if (go.name == entry.TargetObjectName)
            {
                if (!string.IsNullOrEmpty(entry.SceneName)
                    && !entry.SceneName.Equals(currentScene, StringComparison.OrdinalIgnoreCase))
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
                        $"[ObjectConfig] Name match '{go.name}' — scene MISMATCH: config='{entry.SceneName}' actual='{currentScene}'");
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.TargetPath)
                    && !MatchesHierarchyPath(go, entry.TargetPath))
                {
                    KupoUIPRPlugin.PluginLog.LogInfo(
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
            t.localPosition = new Vector3(p.X, p.Y, p.Z);
        }

        if (entry.Rotation.HasValue)
        {
            var r = entry.Rotation.Value;
            t.localEulerAngles = new Vector3(r.X, r.Y, r.Z);
        }

        if (entry.Scale.HasValue)
        {
            var s = entry.Scale.Value;
            t.localScale = new Vector3(s.X, s.Y, s.Z);
        }

        if (entry.SetActive.HasValue)
        {
            go.SetActive(entry.SetActive.Value);
        }

        KupoUIPRPlugin.PluginLog.LogInfo(
            $"[ObjectConfig] Applied rule to '{go.name}' (from {System.IO.Path.GetFileName(entry.SourceFile)})");
    }

    // -------------------------------------------------------------------------
    // Hierarchy path matching (mirrors ScaledDownMenuPatch logic)
    // -------------------------------------------------------------------------

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
            if (current == null || current.name != parts[i])
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
