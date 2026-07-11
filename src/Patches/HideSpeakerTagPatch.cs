using System;
using HarmonyLib;
using UnityEngine;

namespace KupoUI.PR.Patches;

/// <summary>
/// Intercepts GameObject.SetActive. If the active object matches one of the known
/// speaker-tag hierarchy paths, and HideSpeakerTag config is enabled, it moves the
/// object off-screen to hide it.
///
/// Covered paths:
///   - Normal message window:  …/message_window(Clone)/speker_root
///   - Battle message window:  …/battle_message_window(Clone)/message_root/left/speaker
///   - Battle message window:  …/battle_message_window(Clone)/message_root/right/speaker
/// </summary>
[HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
internal static class HideSpeakerTagPatch
{
    // Normal dialogue window — speaker name tag
    private const string TargetObjectName = "speker_root";
    private const string TargetPath = "RootObject/Canvas/UIParent/message_parent(Clone)/parent_root/upper_parent/message_window(Clone)/speker_root";

    // Battle dialogue window — left/right individual speaker tags
    private const string BattleSpeakerObjectName = "speaker";
    private const string BattleLeftSpeakerPath  = "RootObject/Canvas/UIParent/message_parent(Clone)/parent_root/upper_parent/battle_message_window(Clone)/message_root/left/speaker";
    private const string BattleRightSpeakerPath = "RootObject/Canvas/UIParent/message_parent(Clone)/parent_root/upper_parent/battle_message_window(Clone)/message_root/right/speaker";

    private static readonly Vector3 HiddenPosition = new(-730f, -5580f, 0f);
    private static readonly Vector3 HiddenScale = new(0.9f, 0.9f, 1f);

    [HarmonyPostfix]
    private static void SetActivePostfix(GameObject __instance, bool value)
    {
        if (!KupoUIPRPlugin.HideSpeakerTagConfig.Value)
        {
            return;
        }

        if (__instance == null)
        {
            return;
        }

        // Normal message window speaker tag
        if (value && __instance.name == TargetObjectName && MatchesHierarchyPath(__instance, TargetPath))
        {
            __instance.transform.localPosition = HiddenPosition;
            __instance.transform.localScale = HiddenScale;
            return;
        }

        // Battle message window — left and right speaker tags: force inactive
        if (__instance.name == BattleSpeakerObjectName)
        {
            if (MatchesHierarchyPath(__instance, BattleLeftSpeakerPath) ||
                MatchesHierarchyPath(__instance, BattleRightSpeakerPath))
            {
                if (value)
                {
                    __instance.SetActive(false);
                }
            }
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
