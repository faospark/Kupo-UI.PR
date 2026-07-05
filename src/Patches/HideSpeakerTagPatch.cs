using System;
using HarmonyLib;
using UnityEngine;

namespace KupoUI.PR.Patches;

/// <summary>
/// Intercepts GameObject.SetActive. If the active object is "speker_root"
/// (matching the message window speaker tag hierarchy path), and HideSpeakerTag
/// config is enabled, it moves the speaker tag off-screen to hide it.
/// </summary>
[HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
internal static class HideSpeakerTagPatch
{
    private const string TargetObjectName = "speker_root";
    private const string TargetPath = "RootObject/Canvas/UIParent/message_parent(Clone)/parent_root/upper_parent/message_window(Clone)/speker_root";

    private static readonly Vector3 HiddenPosition = new(-730f, -5580f, 0f);
    private static readonly Vector3 HiddenScale = new(0.9f, 0.9f, 1f);

    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (!KupoUIPRPlugin.HideSpeakerTagConfig.Value)
        {
            return;
        }

        if (!value || __instance == null)
        {
            return;
        }

        if (__instance.name == TargetObjectName && MatchesHierarchyPath(__instance, TargetPath))
        {
            __instance.transform.localPosition = HiddenPosition;
            __instance.transform.localScale = HiddenScale;
        }
    }

    /// <summary>
    /// Walks up the transform hierarchy and checks that the path from a root
    /// child matching the expected path layout down to <paramref name="target"/> matches.
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
