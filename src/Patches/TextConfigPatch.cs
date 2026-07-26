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

        private static bool MatchesLanguage(string entryLanguage)
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
    }
}
