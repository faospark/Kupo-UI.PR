using HarmonyLib;
using UnityEngine;

namespace KupoUI.PR.Patches;

[HarmonyPatch]
internal static class ForceVSyncPatch
{
    internal static void ApplyNow()
    {
        if (!KupoUIPRPlugin.ForceVSyncConfig.Value)
        {
            return;
        }

        // Keep Unity's frame pacing on driver-controlled V-Sync.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;
    }

    [HarmonyPatch(typeof(QualitySettings), "vSyncCount", MethodType.Setter)]
    [HarmonyPrefix]
    private static void VSyncCountSetterPrefix(ref int value)
    {
        if (!KupoUIPRPlugin.ForceVSyncConfig.Value)
        {
            return;
        }

        value = 1;
    }

    [HarmonyPatch(typeof(QualitySettings), "vSyncCount", MethodType.Getter)]
    [HarmonyPostfix]
    private static void VSyncCountGetterPostfix(ref int __result)
    {
        if (!KupoUIPRPlugin.ForceVSyncConfig.Value)
        {
            return;
        }

        __result = 1;
    }

    [HarmonyPatch(typeof(Application), "targetFrameRate", MethodType.Setter)]
    [HarmonyPrefix]
    private static void TargetFrameRateSetterPrefix(ref int value)
    {
        if (!KupoUIPRPlugin.ForceVSyncConfig.Value)
        {
            return;
        }

        value = -1;
    }
}