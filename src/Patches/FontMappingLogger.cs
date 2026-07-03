using HarmonyLib;
using Last.Management;
using UnityEngine;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch(typeof(FontManager), "CreateFontParameter")]
    class LogFontParameter_Patch
    {
        static void Postfix(FontManager.FontType __0, Last.Data.Parameters.Language __1, FontManager.FontParameter __result)
        {
            if (__result == null) return;

            // Phase 1: Diagnostic Logging (Always-On by Default)
            if (KupoUIPRPlugin.DiagnosticsLogFontMappingConfig.Value)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[FontMap] FontType={__0} | Language={__1} | FontName={__result.FontName} | LineSpace={__result.LineSpace} | Font={__result.FontInstance?.name}");
            }

            // Phase 2: System Font Swap (Config-Gated)
            if (KupoUIPRPlugin.FontSwapEnabledConfig.Value && KupoUIPRPlugin.ParsedTargetTypes.Contains(__0))
            {
                var systemFont = Font.CreateDynamicFontFromOSFont(
                    KupoUIPRPlugin.FontSwapSystemFontNameConfig.Value, 
                    KupoUIPRPlugin.FontSwapFontSizeConfig.Value
                );

                if (systemFont != null)
                {
                    __result.FontInstance = systemFont;
                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] {__0}/{__1}: swapped to '{KupoUIPRPlugin.FontSwapSystemFontNameConfig.Value}'");
                }
                else
                {
                    KupoUIPRPlugin.PluginLog.LogWarning($"[FontSwap] CreateDynamicFontFromOSFont returned null for '{KupoUIPRPlugin.FontSwapSystemFontNameConfig.Value}' — original font kept.");
                }
            }
        }
    }
}
