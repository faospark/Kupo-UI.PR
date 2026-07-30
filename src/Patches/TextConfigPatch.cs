using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        // Populated by GetMessagePostfix: maps resolved text value → its message key.
        // Used to reverse-look up the real system ID (e.g. MSG_ITEM_NAME_38) from item name strings.
        internal static readonly Dictionary<string, string> ReverseMessageMap = new(StringComparer.Ordinal);
        internal static readonly Dictionary<string, string> _pendingMessageIconSwaps = new(StringComparer.OrdinalIgnoreCase);

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
                        harmony.Patch(updateViewMethod, 
                            prefix: new HarmonyMethod(typeof(TextConfigPatch), nameof(ItemListContentViewUpdateViewPrefix)),
                            postfix: new HarmonyMethod(typeof(TextConfigPatch), nameof(ItemListContentViewUpdateViewPostfix)));
                        KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] Dynamically patched Last.UI.ItemListContentView.UpdateView (Prefix & Postfix).");
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to dynamically patch ItemListContentView: {ex}");
                }
            }

            // Patch Touch/KeyInput item content controllers in battle and main menu
            var cellTypes = new[]
            {
                "Last.UI.Touch.BattleItemContent",
                "Last.UI.KeyInput.BattleItemInfomationContentController",
                "Last.UI.KeyInput.ItemListContentController"
            };

            foreach (var typeName in cellTypes)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t != null)
                {
                    try
                    {
                        var updateViewMethod = AccessTools.Method(t, "UpdateView");
                        if (updateViewMethod != null)
                        {
                            harmony.Patch(updateViewMethod,
                                prefix: new HarmonyMethod(typeof(TextConfigPatch), nameof(BattleItemContentUpdateViewPrefix)),
                                postfix: new HarmonyMethod(typeof(TextConfigPatch), nameof(BattleItemContentUpdateViewPostfix)));
                            KupoUIPRPlugin.PluginLog.LogInfo($"[TextConfig] Dynamically patched {typeName}.UpdateView (Prefix & Postfix).");
                        }
                    }
                    catch (Exception ex)
                    {
                        KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to dynamically patch {typeName}: {ex}");
                    }
                }
            }

            // Patch Last.UI.KeyInput.SelectContentController — drives keyboard/controller battle item list cells on PC!
            var selectCtrlType = AccessTools.TypeByName("Last.UI.KeyInput.SelectContentController");
            if (selectCtrlType != null)
            {
                try
                {
                    var updateViewMethod = AccessTools.Method(selectCtrlType, "UpdateView");
                    if (updateViewMethod != null)
                    {
                        harmony.Patch(updateViewMethod,
                            postfix: new HarmonyMethod(typeof(TextConfigPatch), nameof(KeyInputSelectContentControllerUpdateViewPostfix)));
                        KupoUIPRPlugin.PluginLog.LogInfo("[TextConfig] Dynamically patched Last.UI.KeyInput.SelectContentController.UpdateView (Postfix only).");
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to dynamically patch SelectContentController: {ex}");
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

        internal static void PatchShopListContentData(Harmony harmony)
        {
            var touchControllerType = AccessTools.TypeByName("Last.UI.Touch.ShopListItemContentController");
            var keyInputControllerType = AccessTools.TypeByName("Last.UI.KeyInput.ShopListItemContentController");
            
            var controllerTypes = new[] { touchControllerType, keyInputControllerType };
            foreach (var type in controllerTypes)
            {
                if (type == null) continue;
                try
                {
                    var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var method in methods)
                    {
                        if (method.Name == "UpdateView")
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TextConfigPatch), nameof(ShopListItemUpdateViewPostfix)));
                            if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                                KupoUIPRPlugin.PluginLog.LogInfo($"[TextConfig] Dynamically patched {type.FullName}.UpdateView Postfix.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Failed to patch shop list controller {type.FullName}: {ex}");
                }
            }
        }

        internal static string GetCurrentLanguage()
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

        private static string GetMergedText(string originalText, string newText)
        {
            if (string.IsNullOrEmpty(newText)) return newText;
            var trimmedNew = newText.Trim();
            var onlyTagMatch = Regex.Match(trimmedNew, @"^<(IC_[A-Za-z0-9_]+)>$", RegexOptions.IgnoreCase);
            if (onlyTagMatch.Success)
            {
                string cleanRaw = string.IsNullOrEmpty(originalText) 
                    ? "" 
                    : Regex.Replace(originalText, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).TrimStart();
                return trimmedNew + cleanRaw;
            }
            return newText;
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
                    value = GetMergedText(value, entry.NewText);
                    continue;
                }

                // 2. Match by GameObject Name, Path, SceneName (data-driven override)
                if (string.IsNullOrEmpty(entry.TargetObjectName)) continue;

                if (!IsNameMatch(__instance.gameObject.name, entry.TargetObjectName)) continue;

                if (!string.IsNullOrEmpty(entry.SceneName) && !entry.SceneName.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(entry.TargetPath) && !MatchesHierarchyPath(__instance.gameObject, entry.TargetPath)) continue;

                value = GetMergedText(value, entry.NewText);
            }
        }

        [HarmonyPatch(typeof(Last.Management.MessageManager), nameof(Last.Management.MessageManager.GetMessage), new[] { typeof(string), typeof(bool) })]
        [HarmonyPostfix]
        private static void GetMessagePostfix(string key, bool isReplace, ref string __result)
        {
            // Cache the raw game value before any overrides so we can reverse-look up the real key from a name string.
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(__result))
            {
                string cleanRaw = Regex.Replace(__result, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).TrimStart();
                ReverseMessageMap[cleanRaw] = key;
            }

            var entries = TextConfigLoader.Entries;
            if (entries.Count == 0 || string.IsNullOrEmpty(__result)) return;

            foreach (var entry in entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                // 1. Match by Key
                if (!string.IsNullOrEmpty(entry.Key) && string.Equals(key, entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    __result = GetMergedText(__result, entry.NewText);
                    continue;
                }

                // 2. Match by original value
                if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(__result, entry.OriginalText, StringComparison.Ordinal))
                {
                    __result = GetMergedText(__result, entry.NewText);
                }
            }

            // Stash any custom tag from the final result and rewrite it to <IC_BAG>
            if (!string.IsNullOrEmpty(__result))
            {
                var match = Regex.Match(__result, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string tag = match.Groups[1].Value;
                    // ONLY rewrite if we actually have a custom icon sprite registered for this tag!
                    if (KupoUI.PR.IconsConfig.IconsConfigLoader.HasSprite(tag))
                    {
                        string cleanVal = Regex.Replace(__result, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).Trim();
                        _pendingMessageIconSwaps[key] = tag;
                        _pendingMessageIconSwaps[cleanVal] = tag;
                        __result = "<IC_BAG>" + cleanVal;
                    }
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
        private static void ItemListContentViewUpdateViewPrefix(Il2CppSystem.Object __instance, Last.UI.ItemListContentData data)
        {
            if (__instance == null || data == null) return;
            try
            {
                ItemListContentViewHelper.ProcessUpdateView(__instance, data);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Error in ItemListContentViewUpdateViewPrefix: {ex}");
            }
        }

        private static void ItemListContentViewUpdateViewPostfix(Il2CppSystem.Object __instance, Last.UI.ItemListContentData data)
        {
            if (__instance == null || data == null) return;
            try
            {
                ItemListContentViewHelper.ProcessUpdateViewPostfix(__instance, data);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Error in ItemListContentViewUpdateViewPostfix: {ex}");
            }
        }

        private static void KeyInputSelectContentControllerUpdateViewPostfix(Il2CppSystem.Object __instance, Last.UI.SelectFieldContentManager.SelectFieldContentData data)
        {
            if (__instance == null || data == null) return;
            try
            {
                ItemListContentViewHelper.ProcessSelectFieldContentUpdateViewPostfix(__instance, data);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[KeyInputSelectContent] Error in KeyInputSelectContentControllerUpdateViewPostfix: {ex}");
            }
        }

        // ── BATTLE ITEM VIEW UpdateView HOOKS ────────────────────────────────────────
        private static void BattleItemContentUpdateViewPrefix(Il2CppSystem.Object __instance, Last.UI.ItemListContentData data)
        {
            if (__instance == null || data == null) return;
            try
            {
                ItemListContentViewHelper.ProcessUpdateView(__instance, data);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Error in BattleItemContentUpdateViewPrefix: {ex}");
            }
        }

        private static void BattleItemContentUpdateViewPostfix(Il2CppSystem.Object __instance, Last.UI.ItemListContentData data)
        {
            if (__instance == null || data == null) return;
            try
            {
                ItemListContentViewHelper.ProcessUpdateViewPostfix(__instance, data);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Error in BattleItemContentUpdateViewPostfix: {ex}");
            }
        }

        private static void IconTextViewSetTextPrefix(Il2CppSystem.Object __instance, ref string text)
        {
            if (__instance == null || string.IsNullOrEmpty(text)) return;

            try
            {
                if (KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value)
                {
                    string path = "IconTextView/Name";
                    if (!TextLoggingPatch.IsLogged(path, text))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: 'None' | Value: '{text}'");
                    }
                }

                OverrideByOriginalText(ref text);

                // Run helper for custom inline icon replacement on IconTextView
                IconTextViewHelper.ProcessSetText(__instance, ref text);
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[TextConfig] Error in IconTextView.SetText prefix: {ex}");
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
                    value = GetMergedText(value, entry.NewText);
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

                EnforceText(textComp, GetMergedText(textComp.text, entry.NewText));
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
            // Prefix populates this; menu Postfix reads it — avoids re-scanning entries with hard-coded key formats.
            internal static readonly Dictionary<int, string> _pendingIconSwaps = new();


            internal static void ProcessUpdateView(Il2CppSystem.Object cellObj, Il2CppSystem.Object dataObj)
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
                OverrideItem(itemId, ref overriddenName, ref overriddenDesc, out string matchedNameKey);

                var icMatch = Regex.Match(overriddenName, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                if (icMatch.Success && KupoUI.PR.IconsConfig.IconsConfigLoader.HasSprite(icMatch.Groups[1].Value))
                {
                    string tag = icMatch.Groups[1].Value;
                    _pendingIconSwaps[itemId] = tag;
                    if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                        KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Prefix: item ID {itemId} | tag '{tag}' stashed (key: '{matchedNameKey}')");
                    
                    string nativeName = Regex.Replace(overriddenName, @"<IC_[A-Za-z0-9_]+>", "<IC_BAG>", RegexOptions.IgnoreCase);
                    data._Name_k__BackingField = nativeName;
                }
                else
                {
                    _pendingIconSwaps.Remove(itemId);
                    if (overriddenName != name)
                    {
                        data._Name_k__BackingField = overriddenName;
                    }
                }

                if (overriddenDesc != description)
                {
                    data._Description_k__BackingField = overriddenDesc;
                }
            }

            internal static void ProcessUpdateViewPostfix(Il2CppSystem.Object cellObj, Il2CppSystem.Object dataObj)
            {
                var data = dataObj.TryCast<Last.UI.ItemListContentData>();
                if (data == null) return;

                int itemId = data.ItemId;

                // Resolve the cell transform — works for both ItemListContentView (menu) and
                // BattleItemContent (battle), since we just need a MonoBehaviour to get the transform.
                var mono = cellObj.TryCast<MonoBehaviour>();
                if (mono == null) return;

                // Try menu path first ("icon_text" direct child), then battle path ("root/icon_text").
                Last.UI.IconTextView iconTextComp = null;
                var directSearch = mono.transform.Find("icon_text");
                if (directSearch != null)
                    iconTextComp = directSearch.GetComponent<Last.UI.IconTextView>();

                if (iconTextComp == null)
                {
                    var rootSearch = mono.transform.Find("root/icon_text");
                    if (rootSearch != null)
                        iconTextComp = rootSearch.GetComponent<Last.UI.IconTextView>();
                }

                // Last resort: scan all children for any IconTextView
                if (iconTextComp == null)
                {
                    var all = mono.GetComponentsInChildren<Last.UI.IconTextView>(true);
                    if (all != null && all.Length > 0)
                        iconTextComp = all[0];
                }

                if (iconTextComp == null)
                {
                    if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                        KupoUIPRPlugin.PluginLog.LogWarning($"[IconsConfig] Postfix: no IconTextView found on cell type '{mono.GetType().Name}' for item ID {itemId}.");
                    return;
                }

                // Try the stashed tag first. If not found, fall back to resolving dynamically by clean name text.
                if (!_pendingIconSwaps.TryGetValue(itemId, out string tagName))
                {
                    if (iconTextComp.nameText != null)
                    {
                        string cleanText = GetCleanText(iconTextComp.nameText.text);
                        tagName = GetCustomIconTagForText(cleanText);
                    }
                }

                if (string.IsNullOrEmpty(tagName)) return;

                var sprite = KupoUI.PR.IconsConfig.IconsConfigLoader.GetSprite(tagName);
                if (sprite == null)
                {
                    if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                        KupoUIPRPlugin.PluginLog.LogWarning($"[IconsConfig] Custom icon sprite '{tagName}' on item ID {itemId} is NULL or not loaded.");
                    return;
                }

                if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                    KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Postfix: swapped sprite for tag '{tagName}' on item ID {itemId}.");

                // 1. Activate the icon container (iconBase field) that UseIconImage enables
                var iconBase = iconTextComp.iconBase;
                if (iconBase != null)
                    iconBase.SetActive(true);

                // 2. Assign the custom sprite directly to the iconImage field and enable it
                var img = iconTextComp.iconImage;
                if (img != null)
                {
                    img.sprite = sprite;
                    img.enabled = true;
                    img.gameObject.SetActive(true);
                }

                // 3. Also write directly to icon_root/icon in case iconImage doesn't persist
                var iconRootTr = iconTextComp.transform.Find("icon_root");
                if (iconRootTr != null)
                {
                    var iconTr = iconRootTr.Find("icon");
                    if (iconTr != null)
                    {
                        var iconImg = iconTr.GetComponent<UnityEngine.UI.Image>();
                        if (iconImg != null)
                        {
                            iconImg.sprite = sprite;
                            iconImg.enabled = true;
                            iconImg.gameObject.SetActive(true);
                        }
                    }
                    iconRootTr.gameObject.SetActive(true);
                }

                // 4. Clean the tag out of the nameText field directly
                var nameText = iconTextComp.nameText;
                if (nameText != null)
                {
                    string currentTxt = nameText.text;
                    if (!string.IsNullOrEmpty(currentTxt))
                    {
                        currentTxt = Regex.Replace(currentTxt, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).TrimStart();
                        nameText.text = currentTxt;
                    }
                }
            }

            internal static void ProcessSelectFieldContentUpdateViewPostfix(Il2CppSystem.Object cellObj, Il2CppSystem.Object dataObj)
            {
                var data = dataObj.TryCast<Last.UI.SelectFieldContentManager.SelectFieldContentData>();
                if (data == null) return;

                var controller = cellObj.TryCast<Last.UI.KeyInput.SelectContentController>();
                if (controller == null) return;

                var view = controller.view;
                if (view == null) return;

                var iconTextComp = view.IconTextView;
                if (iconTextComp == null) return;

                string currentText = null;
                if (iconTextComp.nameText != null)
                {
                    currentText = iconTextComp.nameText.text;
                }

                if (string.IsNullOrEmpty(currentText)) return;

                string cleanText = GetCleanText(currentText);
                string tagName = GetCustomIconTagForText(cleanText);

                if (!string.IsNullOrEmpty(tagName))
                {
                    var sprite = KupoUI.PR.IconsConfig.IconsConfigLoader.GetSprite(tagName);
                    if (sprite != null)
                    {
                        if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                            KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] KeyInput SelectField Postfix: matched tag '{tagName}' for text '{cleanText}', sprite found!");

                        var iconBase = iconTextComp.iconBase;
                        if (iconBase != null) iconBase.SetActive(true);

                        var img = iconTextComp.iconImage;
                        if (img != null)
                        {
                            img.sprite = sprite;
                            img.enabled = true;
                            img.gameObject.SetActive(true);
                        }

                        var iconRootTr = iconTextComp.transform.Find("icon_root");
                        if (iconRootTr != null)
                        {
                            var iconTr = iconRootTr.Find("icon");
                            if (iconTr != null)
                            {
                                var iconImg = iconTr.GetComponent<UnityEngine.UI.Image>();
                                if (iconImg != null)
                                {
                                    iconImg.sprite = sprite;
                                    iconImg.enabled = true;
                                    iconImg.gameObject.SetActive(true);
                                }
                            }
                            iconRootTr.gameObject.SetActive(true);
                        }

                        var nameText = iconTextComp.nameText;
                        if (nameText != null)
                        {
                            string raw = nameText.text;
                            if (!string.IsNullOrEmpty(raw))
                            {
                                nameText.text = Regex.Replace(raw, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).TrimStart();
                            }
                        }
                    }
                }
            }

            private static string GetCleanText(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                return Regex.Replace(text, @"<IC_[A-Za-z0-9_]+>", "", RegexOptions.IgnoreCase).TrimStart();
            }

            private static void LogItem(int itemId, string name, string description)
            {
                if (!KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value) return;

                if (!string.IsNullOrEmpty(name))
                {
                    string cleanName = GetCleanText(name);
                    // Resolve real system key via reverse map (e.g. MSG_ITEM_NAME_38 not MSG_ITEM_9)
                    string realNameKey = TextConfigPatch.ReverseMessageMap.TryGetValue(cleanName, out var rk) ? rk : $"MSG_ITEM_{itemId}";
                    string path = $"Inventory/ItemSlot/{itemId}/Name";
                    if (!TextLoggingPatch.IsLogged(path, name))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: '{realNameKey}' | Value: '{name}'");
                    }
                }

                if (!string.IsNullOrEmpty(description))
                {
                    string cleanDesc = GetCleanText(description);
                    string realDescKey = TextConfigPatch.ReverseMessageMap.TryGetValue(cleanDesc, out var dk) ? dk : $"MSG_ITEM_DESC_{itemId}";
                    string path = $"Inventory/ItemSlot/{itemId}/Description";
                    if (!TextLoggingPatch.IsLogged(path, description))
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: '{realDescKey}' | Value: '{description}'");
                    }
                }
            }

            internal static void OverrideItem(int itemId, ref string name, ref string description, out string matchedNameKey)
            {
                matchedNameKey = null;
                var entries = TextConfigLoader.Entries;
                if (entries.Count == 0) return;

                string cleanName = GetCleanText(name);
                string cleanDesc = GetCleanText(description);

                // Prefer the real system key resolved from the reverse map (e.g. MSG_ITEM_NAME_38).
                // Fall back to slot-position keys only if not found.
                string realNameKey = TextConfigPatch.ReverseMessageMap.TryGetValue(cleanName, out var rk) ? rk : null;
                string realDescKey = TextConfigPatch.ReverseMessageMap.TryGetValue(cleanDesc, out var dk) ? dk : null;

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
                            matchedNameKey = $"(OriginalText) {entry.OriginalText}";
                            name = GetMergedText(name, entry.NewText);
                        }
                        else if (!string.IsNullOrEmpty(entry.Key) &&
                                 (// Real system key (e.g. MSG_ITEM_NAME_38)
                                  (!string.IsNullOrEmpty(realNameKey) && string.Equals(entry.Key, realNameKey, StringComparison.OrdinalIgnoreCase)) ||
                                  // Slot-position fallback keys
                                  string.Equals(entry.Key, nameKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(entry.Key, nameAltKey, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchedNameKey = entry.Key;
                            name = GetMergedText(name, entry.NewText);
                        }
                    }

                    // 2. Rewrite Description
                    if (!string.IsNullOrEmpty(description))
                    {
                        if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(description, entry.OriginalText, StringComparison.Ordinal))
                        {
                            description = GetMergedText(description, entry.NewText);
                        }
                        else if (!string.IsNullOrEmpty(entry.Key) &&
                                 ((!string.IsNullOrEmpty(realDescKey) && string.Equals(entry.Key, realDescKey, StringComparison.OrdinalIgnoreCase)) ||
                                  string.Equals(entry.Key, descKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(entry.Key, descAltKey, StringComparison.OrdinalIgnoreCase)))
                        {
                            description = GetMergedText(description, entry.NewText);
                        }
                    }
                }
            }
        }

        // Nested helper class to isolate JIT references to Last.UI.IconTextView
        private static class IconTextViewHelper
        {
            internal static void ProcessSetText(Il2CppSystem.Object instance, ref string text)
            {
                var iconText = instance.TryCast<Last.UI.IconTextView>();
                if (iconText == null || string.IsNullOrEmpty(text)) return;

                // Match tag pattern <(IC_[A-Za-z0-9_]+)> case-insensitively
                var match = Regex.Match(text, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string tagName = match.Groups[1].Value;
                    string cleanText = text.Replace(match.Value, "").Trim();
                    string customTag = GetCustomIconTagForText(cleanText);

                    KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] ProcessSetText info: text='{text}', cleanText='{cleanText}', tagName='{tagName}', customTag='{customTag}'");

                    // If it is the native placeholder tag <IC_BAG>, try to resolve to the custom stashed tag
                    if (string.Equals(tagName, "IC_BAG", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(customTag))
                        {
                            tagName = customTag;
                        }
                    }

                    var sprite = KupoUI.PR.IconsConfig.IconsConfigLoader.GetSprite(tagName);
                    if (sprite != null)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] ProcessSetText: matched tag '{tagName}', sprite found!");
                        
                        // Clean the tag out of the text (so the text does not contain it)
                        text = text.Replace(match.Value, "").TrimStart();

                        // Use the native SetIconImage and UseIconImage
                        iconText.SetIconImage(sprite);
                        iconText.UseIconImage();
                    }
                    else
                    {
                        KupoUIPRPlugin.PluginLog.LogWarning($"[IconsConfig] ProcessSetText: sprite for tag '{tagName}' is not loaded.");
                    }
                }
            }
        }

        private static string GetCustomIconTagForText(string cleanText)
        {
            if (string.IsNullOrEmpty(cleanText)) return null;

            // 1. Resolve key from ReverseMessageMap
            if (ReverseMessageMap.TryGetValue(cleanText, out var key))
            {
                foreach (var entry in TextConfigLoader.Entries)
                {
                    if (!MatchesLanguage(entry.Language)) continue;

                    if (!string.IsNullOrEmpty(entry.Key) && string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(entry.NewText, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }

            // 2. Resolve by matching OriginalText rules
            foreach (var entry in TextConfigLoader.Entries)
            {
                if (!MatchesLanguage(entry.Language)) continue;

                if (!string.IsNullOrEmpty(entry.OriginalText) && string.Equals(cleanText, entry.OriginalText, StringComparison.Ordinal))
                {
                    var match = Regex.Match(entry.NewText, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        private static void ShopListItemUpdateViewPostfix(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null) return;

                var prop = AccessTools.Property(__instance.GetType(), "iconTextView");
                if (prop == null) return;
                
                var iconTextVal = prop.GetValue(__instance);
                if (iconTextVal == null) return;
                
                var iconTextComp = iconTextVal as Last.UI.IconTextView;
                if (iconTextComp == null) return;

                var nameText = iconTextComp.nameText;
                if (nameText == null || string.IsNullOrEmpty(nameText.text)) return;

                string text = nameText.text;
                string tagName = null;

                // 1. Try to extract tag directly from the text component if present
                var match = Regex.Match(text, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    tagName = match.Groups[1].Value;
                    // Clean the tag out of the text
                    nameText.text = text.Replace(match.Value, "").TrimStart();
                }
                else
                {
                    // 2. Otherwise, look up via custom config entries using the clean text
                    tagName = GetCustomIconTagForText(text);
                }

                if (!string.IsNullOrEmpty(tagName))
                {
                    var sprite = KupoUI.PR.IconsConfig.IconsConfigLoader.GetSprite(tagName);
                    if (sprite != null)
                    {
                        if (KupoUIPRPlugin.DiagnosticIconLoggingConfig.Value)
                            KupoUIPRPlugin.PluginLog.LogInfo($"[IconsConfig] Shop UpdateView Postfix: matched tag '{tagName}', applying custom sprite.");
                        
                        iconTextComp.SetIconImage(sprite);
                        iconTextComp.UseIconImage();
                    }
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[IconsConfig] Error in ShopListItemUpdateViewPostfix: {ex}");
            }
        }
    }
}
