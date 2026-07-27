using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using KupoUI.PR.IconsConfig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch]
    internal static class TextIconPatch
    {
        private struct IconTagInfo
        {
            public string TagName;
            public int CharIndex;
        }

        private static readonly ConcurrentDictionary<IntPtr, List<IconTagInfo>> _activeTextIcons = new();
        private static bool _isCleaningText;

        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        private static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (__instance == null || _isCleaningText || string.IsNullOrEmpty(value)) return;

            if (value.IndexOf("<IC_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string cleaned = ProcessTextTags(__instance, value, out var tagInfos);
                if (tagInfos.Count > 0)
                {
                    _activeTextIcons[__instance.Pointer] = tagInfos;
                    value = cleaned;
                }
            }
        }

        [HarmonyPatch(typeof(Text), "OnPopulateMesh", new[] { typeof(VertexHelper) })]
        [HarmonyPrefix]
        private static void OnPopulateMeshPrefix(Text __instance)
        {
            if (__instance == null || _isCleaningText) return;

            string currentText = __instance.text;
            if (string.IsNullOrEmpty(currentText)) return;

            if (currentText.IndexOf("<IC_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _isCleaningText = true;
                try
                {
                    string cleaned = ProcessTextTags(__instance, currentText, out var tagInfos);
                    if (tagInfos.Count > 0)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[IconsEngine] OnPopulateMeshPrefix matched tags in '{__instance.gameObject.name}'. Cleaned: '{cleaned}'");
                        _activeTextIcons[__instance.Pointer] = tagInfos;
                        __instance.text = cleaned;
                    }
                }
                finally
                {
                    _isCleaningText = false;
                }
            }
        }

        [HarmonyPatch(typeof(Text), "OnPopulateMesh", new[] { typeof(VertexHelper) })]
        [HarmonyPostfix]
        private static void OnPopulateMeshPostfix(Text __instance)
        {
            if (__instance == null) return;
            DrawIcons(__instance);
        }

        [HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
        [HarmonyPostfix]
        private static void SceneLoadedPostfix()
        {
            _activeTextIcons.Clear();
        }

        private static string ProcessTextTags(Text textComp, string originalText, out List<IconTagInfo> tagInfos)
        {
            tagInfos = new List<IconTagInfo>();
            if (string.IsNullOrEmpty(originalText)) return originalText;

            var matches = Regex.Matches(originalText, @"<(IC_[A-Za-z0-9_]+)>", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return originalText;

            string cleanedText = originalText;
            int offset = 0;

            foreach (Match match in matches)
            {
                string fullTag = match.Value;
                string tagName = match.Groups[1].Value;

                var sprite = IconsConfigLoader.GetSprite(tagName);
                if (sprite == null) continue;

                int originalIndex = match.Index;
                int cleanedIndex = originalIndex - offset;

                string spacer = "    "; // 4 spaces is a great default spacer
                cleanedText = cleanedText.Remove(cleanedIndex, fullTag.Length).Insert(cleanedIndex, spacer);

                tagInfos.Add(new IconTagInfo
                {
                    TagName = tagName,
                    CharIndex = cleanedIndex
                });

                offset += (fullTag.Length - spacer.Length);
            }

            return cleanedText;
        }

        private static void DrawIcons(Text textComp)
        {
            if (textComp == null) return;

            if (!_activeTextIcons.TryGetValue(textComp.Pointer, out var tagList) || tagList.Count == 0)
            {
                DisableAllIcons(textComp);
                return;
            }

            int characterCount = textComp.cachedTextGenerator.characterCount;
            int i = 0;

            for (; i < tagList.Count; i++)
            {
                var tagInfo = tagList[i];
                int targetIndex = tagInfo.CharIndex;

                if (targetIndex >= 0 && targetIndex < characterCount)
                {
                    var charInfo = textComp.cachedTextGenerator.characters[targetIndex];
                    Vector2 localPos = charInfo.cursorPos;

                    string childName = $"__KupoIcon_{i}";
                    Transform child = textComp.transform.Find(childName);
                    if (child == null)
                    {
                        KupoUIPRPlugin.PluginLog.LogInfo($"[IconsEngine] Creating child Image GameObject '{childName}' under '{textComp.gameObject.name}'");
                        GameObject go = new GameObject(childName);
                        go.AddComponent<CanvasRenderer>();
                        go.AddComponent<Image>();
                        go.transform.SetParent(textComp.transform, false);
                        child = go.transform;
                    }

                    child.gameObject.SetActive(true);
                    var img = child.GetComponent<Image>();
                    img.sprite = IconsConfigLoader.GetSprite(tagInfo.TagName);
                    img.color = Color.white;

                    var rect = child.GetComponent<RectTransform>();
                    var parentPivot = textComp.rectTransform.pivot;
                    rect.anchorMin = parentPivot;
                    rect.anchorMax = parentPivot;
                    rect.pivot = new Vector2(0f, 0.5f);
                    float referenceFontSize = textComp.fontSize > 0 ? textComp.fontSize : 12f;
                    float iconSize = referenceFontSize;
                    rect.sizeDelta = new Vector2(iconSize, iconSize);
                    rect.localScale = Vector3.one;

                    float verticalOffset = -referenceFontSize * 0.5f;
                    rect.anchoredPosition = new Vector2(localPos.x, localPos.y + verticalOffset);
                }
            }

            for (; ; i++)
            {
                string extraChildName = $"__KupoIcon_{i}";
                Transform extraChild = textComp.transform.Find(extraChildName);
                if (extraChild == null) break;
                extraChild.gameObject.SetActive(false);
            }
        }

        private static void DisableAllIcons(Text textComp)
        {
            for (int i = 0; ; i++)
            {
                string childName = $"__KupoIcon_{i}";
                Transform child = textComp.transform.Find(childName);
                if (child == null) break;
                child.gameObject.SetActive(false);
            }
        }
    }
}
