using System;
using HarmonyLib;
using Last.Management;
using UnityEngine;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch(typeof(FontManager), "GetFont")]
    class FontInstance_Swap_Patch
    {
        [HarmonyPostfix]
        static void Postfix(FontManager.FontType type, FontManager.FontParameter __result)
        {
            if (__result == null) return;
            if (!KupoUIPRPlugin.FontSwapEnabledConfig.Value) return;

            if (!KupoUIPRPlugin.FontConfigMapping.TryGetValue(type, out var configEntry)) return;

            var fontName = configEntry.FontName;
            if (string.IsNullOrEmpty(fontName)) return;

            // Determine target font size: check config first, fallback to original font size, fallback to 32.
            int targetSize = 32;
            if (configEntry.FontSize.HasValue)
            {
                targetSize = configEntry.FontSize.Value;
            }
            else if (__result.FontInstance != null && __result.FontInstance.fontSize > 0)
            {
                targetSize = __result.FontInstance.fontSize;
            }

            var fontInstance = GetOrCreateFont(fontName, targetSize);
            if (fontInstance != null)
            {
                // If already swapped, skip to avoid redundant assignments
                if (__result.FontInstance != null && __result.FontInstance.Pointer == fontInstance.Pointer)
                {
                    return;
                }

                try
                {
                    __result.FontInstance = fontInstance;

                    if (configEntry.LineSpace.HasValue)
                    {
                        __result.LineSpace = configEntry.LineSpace.Value;
                    }

                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] GetFont Postfix: FontType={type} swapped to '{fontName}' at size {targetSize} (LineSpace={__result.LineSpace})");
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to assign FontInstance: {ex}");
                }
            }
        }

        private static Font GetOrCreateFont(string fontName, int fontSize)
        {
            var cacheKey = $"{fontName}_{fontSize}";
            if (KupoUIPRPlugin.LoadedFonts.TryGetValue(cacheKey, out var fontInstance))
            {
                return fontInstance;
            }

            try
            {
                fontInstance = Font.CreateDynamicFontFromOSFont(fontName, fontSize);
                if (fontInstance != null)
                {
                    KupoUIPRPlugin.LoadedFonts[cacheKey] = fontInstance;
                    KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Successfully created dynamic font from OS: '{fontName}' at size {fontSize}");
                    return fontInstance;
                }
                else
                {
                    KupoUIPRPlugin.PluginLog.LogWarning($"[FontSwap] CreateDynamicFontFromOSFont returned null for '{fontName}' at size {fontSize}. Original font will be kept.");
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to create dynamic font '{fontName}': {ex}");
            }

            return null;
        }
    }
}
