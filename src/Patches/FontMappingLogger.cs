using HarmonyLib;
using Last.Management;

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
                KupoUIPRPlugin.PluginLog.LogInfo($"[FontMap] FontType={__0} | Language={__1} | FontName={__result.FontName} | LineSpace={__result.LineSpace} | FontInstance={__result.FontInstance?.name}");
            }
        }
    }
}
