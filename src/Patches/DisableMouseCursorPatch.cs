using HarmonyLib;
using UnityEngine;

namespace KupoUI.PR.Patches;

[HarmonyPatch(typeof(Cursor))]
internal static class DisableMouseCursorPatch
{
    [HarmonyPatch("visible", MethodType.Setter)]
    [HarmonyPrefix]
    private static void CursorVisiblePrefix(ref bool value)
    {
        if (!KupoUIPRPlugin.DisableMouseCursorConfig.Value)
        {
            return;
        }

        value = false;
    }

    [HarmonyPatch("visible", MethodType.Getter)]
    [HarmonyPostfix]
    private static void CursorVisiblePostfix(ref bool __result)
    {
        if (!KupoUIPRPlugin.DisableMouseCursorConfig.Value)
        {
            return;
        }

        __result = false;
    }
}
