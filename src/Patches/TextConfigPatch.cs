using System;
using HarmonyLib;
using KupoUI.PR.TextConfig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch]
    internal static class TextConfigPatch
    {
        private static bool _isApplyingText;
        private static string _cachedLanguage;

        internal static void Initialize(string modulesRootPath)
        {
            TextConfigLoader.Load(modulesRootPath);
            KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] Patch initialized.");
        }

        internal static void PatchItemListContentData(Harmony harmony)
        {
            var viewType = AccessTools.TypeByName("Last.UI.ItemListContentView");
            if (viewType != null)
            {
                try
                {
                    var updateViewMethod = AccessTools.Method(viewType, "UpdateView");
                    if (updateViewMethod != null)
                    {
                        harmony.Patch(updateViewMethod, prefix: new HarmonyMethod(typeof(TextConfigPatch), nameof(ItemListContentViewUpdateViewPrefix)));
                        KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] Dynamically patched Last.UI.ItemListContentView.UpdateView.");
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to dynamically patch ItemListContentView: {ex}");
                }
            }

            var iconTextViewType = AccessTools.TypeByName("Last.UI.IconTextView");
            if (iconTextViewType != null)
            {
                try
                {
                    var setTextMethod = AccessTools.Method(iconTextViewType, "SetText", new[] { typeof(string) });
                    if (setTextMethod != null)
                    {
                        harmony.Patch(setTextMethod, prefix: new HarmonyMethod(typeof(TextConfigPatch), nameof(IconTextViewSetTextPrefix)));
                        KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] Dynamically patched Last.UI.IconTextView.SetText.");
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to dynamically patch IconTextView: {ex}");
                }
            }
        }

        private static string GetCurrentLanguage()
        {
            if (_cachedLanguage != null) return _cachedLanguage;
            try
            {
                var msgMgr = UnityEngine.Object.FindObjectOfType<Last.Management.MessageManager>();
                if (msgMgr != null)
                {
                    _cachedLanguage = msgMgr.currentLanguage.ToString();
                    return _cachedLanguage;
                }
            }
            catch { }
            return "En";
        }

        internal static bool MatchesLanguage(string entryLanguage)
        {
            if (string.IsNullOrEmpty(entryLanguage)) return true;
            return string.Equals(entryLanguage, GetCurrentLanguage(), StringComparison.OrdinalIgnoreCase);
        }

        // ── HOOK 1: INTERCEPT TEXT.TEXT SETTER ──────────────────────────────────
        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        private static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (_isApplyingText || __instance == null) return;

            var entries = TextConfigLoader.Entries;
            if (entries.Count == 0) return;

            var sceneName = SceneManager.GetActiveScene().name;

            foreach (var entry in entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                // 1. Match by original value
                if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(value, entry.OriginalText, StringComparison.Ordinal))
                {
                    value = entry.NewText;
                    continue;
                }

                // 2. Match by GameObject Name, Path, SceneName (data-driven override)
                if (string.IsNullOrEmpty(entry.TargetObjectName)) continue;

                if (!IsNameMatch(__instance.gameObject.name, entry.TargetObjectName)) continue;

                if (!string.IsNullOrEmpty(entry.SceneName) && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(entry.TargetPath) && !MatchesHierarchyPath(__instance.gameObject, entry.TargetPath)) continue;

                value = entry.NewText;
            }
        }

        // ── HOOK 2: INTERCEPT MESSAGEMANAGER.GETMESSAGE ──────────────────────────
        [HarmonyPatch(typeof(Last.Management.MessageManager), nameof(Last.Management.MessageManager.GetMessage), new[] { typeof(string), typeof(bool) })]
        [HarmonyPostfix]
        private static void GetMessagePostfix(string key, bool isReplace, ref string __result)
        {
            var entries = TextConfigLoader.Entries;
            if (entries.Count == 0 || string.IsNullOrEmpty(__result)) return;

            foreach (var entry in entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                // 1. Match by Key
                if (!string.IsNullOrEmpty(entry.Key) && string.Equals(key, entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    __result = entry.NewText;
                    continue;
                }

                // 2. Match by original value
                if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(__result, entry.OriginalText, StringComparison.Ordinal))
                {
                    __result = entry.NewText;
                }
            }
        }

        // ── HOOK 3: INITIAL SWEEP ON SCENE LOAD ────────────────────────────────
        [HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
        [HarmonyPostfix]
        private static void SceneLoadedPostfix(Scene scene, LoadSceneMode mode)
        {
            _cachedLanguage = null; // Clear cached language to capture any dynamic configuration updates
            var sceneName = scene.name;
            var rootObjects = scene.GetRootGameObjects();

            foreach (var root in rootObjects)
            {
                ApplyToHierarchy(root, sceneName);
            }
        }

        // ── HOOK 4: APPLY TO GRAPHIC/TEXT ON ENABLE (FOR DYNAMIC OBJECTS) ───────
        [HarmonyPatch(typeof(Graphic), "OnEnable")]
        [HarmonyPostfix]
        private static void GraphicOnEnablePostfix(Graphic __instance)
        {
            if (__instance == null || __instance.gameObject == null) return;
            if (__instance is Text textComp)
            {
                ApplyMatchingRules(textComp.gameObject, SceneManager.GetActiveScene().name);
            }
        }

        // ── DYNAMIC ITEM OVERRIDES PREFIXES ──────────────────────────────────
        private static void ItemListContentViewUpdateViewPrefix(object[] __args)
        {
            if (__args == null || __args.Length == 0) return;

            try
            {
                var dataObj = __args[0] as Il2CppSystem.Object;
                if (dataObj != null && dataObj.Pointer != IntPtr.Zero)
                {
                    ItemListContentViewHelper.ProcessUpdateView(dataObj);
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Error in UpdateView prefix: {ex}");
            }
        }

        private static void IconTextViewSetTextPrefix(object[] __args)
        {
            if (__args == null || __args.Length == 0) return;

            try
            {
                string value = __args[0] as string;
                if (string.IsNullOrEmpty(value)) return;

                if (KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value)
                {
                    string path = "IconTextView/Name";
                    if (!TextLoggingPatch.IsLogged(path, value))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: 'None' | Value: '{value}'");
                    }
                }

                string newValue = value;
                OverrideByOriginalText(ref newValue);
                __args[0] = newValue;
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Error in SetText prefix: {ex}");
            }
        }

        private static void OverrideByOriginalText(ref string value)
        {
            var entries = TextConfigLoader.Entries;
            if (entries.Count == 0) return;

            foreach (var entry in entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(value, entry.OriginalText, StringComparison.Ordinal))
                {
                    value = entry.NewText;
                    return;
                }
            }
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private static void ApplyToHierarchy(GameObject go, string sceneName)
        {
            if (go == null) return;

            var allTexts = go.GetComponentsInChildren<Text>(includeInactive: true);
            foreach (var text in allTexts)
            {
                if (text != null && text.gameObject != null)
                {
                    ApplyMatchingRules(text.gameObject, sceneName);
                }
            }
        }

        private static void ApplyMatchingRules(GameObject go, string currentScene)
        {
            if (go == null) return;

            var entries = TextConfigLoader.Entries;
            if (entries.Count == 0) return;

            var textComp = go.GetComponent<Text>();
            if (textComp == null) return;

            foreach (var entry in entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                if (string.IsNullOrEmpty(entry.TargetObjectName)) continue;

                if (!IsNameMatch(go.name, entry.TargetObjectName)) continue;

                if (!string.IsNullOrEmpty(entry.SceneName) && !entry.SceneName.Equals(currentScene, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(entry.TargetPath) && !MatchesHierarchyPath(go, entry.TargetPath)) continue;

                EnforceText(textComp, entry.NewText);
            }
        }

        private static void EnforceText(Text textComp, string newText)
        {
            if (_isApplyingText || textComp == null) return;
            _isApplyingText = true;
            try
            {
                textComp.text = newText;
            }
            finally
            {
                _isApplyingText = false;
            }
        }

        private static bool IsNameMatch(string name1, string name2)
        {
            if (name1 == null || name2 == null) return false;
            if (string.Equals(name1, name2, StringComparison.Ordinal)) return true;
            return string.Equals(name1.Trim(), name2.Trim(), StringComparison.Ordinal);
        }

        private static bool MatchesHierarchyPath(GameObject target, string expectedPath)
        {
            var parts = expectedPath.Split('/');
            var current = target.transform;
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (current == null || !IsNameMatch(current.name, parts[i]))
                {
                    return false;
                }
                current = current.parent;
            }
            return true;
        }

        // Nested helper class to isolate JIT references to Last.UI.ItemListContentData
        private static class ItemListContentViewHelper
        {
            internal static void ProcessUpdateView(Il2CppSystem.Object dataObj)
            {
                var data = dataObj.TryCast<Last.UI.ItemListContentData>();
                if (data == null) return;

                int itemId = data.ItemId;
                string name = data.Name;
                string description = data.Description;

                // Log details
                LogItem(itemId, name, description);

                // Override details
                string overriddenName = name;
                string overriddenDesc = description;
                OverrideItem(itemId, ref overriddenName, ref overriddenDesc);

                if (overriddenName != name)
                {
                    data._Name_k__BackingField = overriddenName;
                }
                if (overriddenDesc != description)
                {
                    data._Description_k__BackingField = overriddenDesc;
                }
            }

            private static void LogItem(int itemId, string name, string description)
            {
                if (!KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value) return;

                if (!string.IsNullOrEmpty(name))
                {
                    string key = $"MSG_ITEM_{itemId}";
                    string path = $"Inventory/ItemSlot/{itemId}/Name";
                    if (!TextLoggingPatch.IsLogged(path, name))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: '{key}' | Value: '{name}'");
                    }
                }

                if (!string.IsNullOrEmpty(description))
                {
                    string key = $"MSG_ITEM_DESC_{itemId}";
                    string path = $"Inventory/ItemSlot/{itemId}/Description";
                    if (!TextLoggingPatch.IsLogged(path, description))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: '{key}' | Value: '{description}'");
                    }
                }
            }

            private static void OverrideItem(int itemId, ref string name, ref string description)
            {
                var entries = TextConfigLoader.Entries;
                if (entries.Count == 0) return;

                string nameKey = $"MSG_ITEM_{itemId}";
                string nameAltKey = $"ITEM_{itemId}";
                string descKey = $"MSG_ITEM_DESC_{itemId}";
                string descAltKey = $"ITEM_DESC_{itemId}";

                foreach (var entry in entries)
                {
                    if (!TextConfigPatch.MatchesLanguage(entry.Language)) continue;

                    // 1. Rewrite Name
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(name, entry.OriginalText, StringComparison.Ordinal))
                        {
                            name = entry.NewText;
                        }
                        else if (!string.IsNullOrEmpty(entry.Key) &&
                                 (string.Equals(entry.Key, nameKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(entry.Key, nameAltKey, StringComparison.OrdinalIgnoreCase)))
                        {
                            name = entry.NewText;
                        }
                    }

                    // 2. Rewrite Description
                    if (!string.IsNullOrEmpty(description))
                    {
                        if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(description, entry.OriginalText, StringComparison.Ordinal))
                        {
                            description = entry.NewText;
                        }
                        else if (!string.IsNullOrEmpty(entry.Key) &&
                                 (string.Equals(entry.Key, descKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(entry.Key, descAltKey, StringComparison.OrdinalIgnoreCase)))
                        {
                            description = entry.NewText;
                        }
                    }
                }
            }
        }
    }
}
