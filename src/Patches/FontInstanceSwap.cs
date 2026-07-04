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
        static void Postfix(FontManager __instance, FontManager.FontType type, FontManager.FontParameter __result)
        {
            if (__result == null) return;
            if (!KupoUIPRPlugin.FontSwapEnabledConfig.Value) return;

            // Retrieve the language associated with this FontParameter instance.
            string language = null;
            KupoUIPRPlugin.FontParameterLanguages.TryGetValue(__result.Pointer, out language);

            if (!KupoUIPRPlugin.TryGetFontConfig(type, language, out var configEntry)) return;

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
                // Overwrite the FontInstance property
                try
                {
                    if (__result.FontInstance == null || __result.FontInstance.Pointer != fontInstance.Pointer)
                    {
                        __result.FontInstance = fontInstance;
                        if (configEntry.LineSpace.HasValue)
                        {
                            __result.LineSpace = configEntry.LineSpace.Value;
                        }
                        KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] GetFont Postfix: FontType={type} (Language={language ?? "Default"}) swapped to '{fontName}' at size {targetSize} (LineSpace={__result.LineSpace})");
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to assign FontInstance: {ex}");
                }

                // Overwrite the key in FontManager's cacheFontList dictionary
                try
                {
                    var cache = __instance.cacheFontList;
                    if (cache != null && !string.IsNullOrEmpty(__result.FontName))
                    {
                        bool needsUpdate = true;
                        if (cache.TryGetValue(__result.FontName, out var existingFont))
                        {
                            if (existingFont != null && existingFont.Pointer == fontInstance.Pointer)
                            {
                                needsUpdate = false;
                            }
                        }

                        if (needsUpdate)
                        {
                            // Log keys for diagnostics on actual change
                            if (KupoUIPRPlugin.DiagnosticsLogFontMappingConfig.Value)
                            {
                                var keys = new System.Collections.Generic.List<string>();
                                var enumerator = cache.Keys.GetEnumerator();
                                while (enumerator.MoveNext())
                                {
                                    keys.Add(enumerator.Current);
                                }
                                KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Current cacheFontList keys: {string.Join(", ", keys)}");
                            }

                            if (cache.ContainsKey(__result.FontName))
                            {
                                cache.Remove(__result.FontName);
                            }
                            cache.Add(__result.FontName, fontInstance);
                            KupoUIPRPlugin.PluginLog.LogInfo($"[FontSwap] Updated cacheFontList key '{__result.FontName}' -> '{fontName}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[FontSwap] Failed to update cacheFontList: {ex}");
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
