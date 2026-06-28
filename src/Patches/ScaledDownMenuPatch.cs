using HarmonyLib;
using UnityEngine;

namespace KupoUI.PR.Patches;

[HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
internal static class ScaledDownMenuPatch
{
    private const string TargetObjectName = "menu_base(Clone)";
    private const string TargetPath = "Canvas/aspect_parent/menu_parent/menu_base(Clone)";

    private static readonly Vector3 ScaledDownScale = new(0.9f, 0.9f, 1f);

    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (!KupoUIPRPlugin.ScaledDownMenuConfig.Value)
        {
            return;
        }

        if (!value)
        {
            return;
        }

        if (__instance.name != TargetObjectName)
        {
            return;
        }

        // Verify the full hierarchy path to avoid hitting unrelated objects with the same name.
        if (!MatchesHierarchyPath(__instance, TargetPath))
        {
            return;
        }

        __instance.transform.localScale = ScaledDownScale;
    }

    /// <summary>
    /// Walks up the transform hierarchy and checks that the path from a root
    /// child named "Canvas" down to <paramref name="target"/> matches
    /// <paramref name="expectedPath"/>.
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
}
