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

        // Cached icon overlay state per Text component.
        private sealed class IconOverlayState
        {
            // The tag list parsed from the last text assignment.
            public List<IconTagInfo> Tags;
            // Cached child objects, indexed by slot. Avoids transform.Find and GetComponent every frame.
            public readonly List<(GameObject Go, Image Img, RectTransform Rect)> Slots = new();
        }

        // [OPT-PERF] Pre-compiled, statically-allocated Regex. Previously ProcessTextTags created a
        // new (interpreted) Regex object on every call — which fires once per visible Text per redraw.
        private static readonly Regex IcTagRegex = new(@"<(IC_[A-Za-z0-9_]+)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Maps Text pointer → overlay state (tags + cached child slots).
        // Replaces the two separate ConcurrentDictionaries.
        private static readonly ConcurrentDictionary<IntPtr, IconOverlayState> _overlayStates = new();

        // [OPT-PERF] Tracks pointers of Text components whose *current* text contains no IC_ tags and
        // have never had icon children. Used to exit OnPopulateMesh in O(1) for the vast majority of
        // text elements in the menu without reading the .text property or doing any string work.
        private static readonly ConcurrentDictionary<IntPtr, bool> _confirmedNoIcons = new();

        private static bool _isCleaningText;

        // ── HARMONY HOOKS ────────────────────────────────────────────────────

        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        private static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (__instance == null || _isCleaningText || string.IsNullOrEmpty(value)) return;

            var ptr = __instance.Pointer;

            if (value.IndexOf("<IC_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Text might have icons — remove from the fast-exit set and process.
                _confirmedNoIcons.TryRemove(ptr, out _);
                string cleaned = ProcessTextTags(value, out var tagInfos);
                if (tagInfos != null && tagInfos.Count > 0)
                {
                    GetOrCreateOverlay(ptr).Tags = tagInfos;
                    value = cleaned;
                }
            }
            else
            {
                // No IC_ tag in this assignment — if this pointer had overlay state, clear only the
                // tags (keep the slot cache alive for reuse). Mark as confirmed-no-icons.
                if (_overlayStates.TryGetValue(ptr, out var state))
                {
                    state.Tags = null;
                }
                _confirmedNoIcons.TryAdd(ptr, true);
            }
        }

        [HarmonyPatch(typeof(Text), "OnPopulateMesh", new[] { typeof(VertexHelper) })]
        [HarmonyPrefix]
        private static void OnPopulateMeshPrefix(Text __instance)
        {
            if (__instance == null || _isCleaningText) return;

            var ptr = __instance.Pointer;

            // [OPT-PERF] Fast-exit: we have already confirmed this component carries no IC_ tags.
            if (_confirmedNoIcons.ContainsKey(ptr)) return;

            string currentText = __instance.text;
            if (string.IsNullOrEmpty(currentText)) return;

            if (currentText.IndexOf("<IC_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _isCleaningText = true;
                try
                {
                    string cleaned = ProcessTextTags(currentText, out var tagInfos);
                    if (tagInfos != null && tagInfos.Count > 0)
                    {
                        GetOrCreateOverlay(ptr).Tags = tagInfos;
                        __instance.text = cleaned;
                    }
                }
                finally
                {
                    _isCleaningText = false;
                }
            }
            else
            {
                // Confirm no icons so future redraws skip all work.
                if (_overlayStates.TryGetValue(ptr, out var state))
                {
                    state.Tags = null;
                }
                _confirmedNoIcons.TryAdd(ptr, true);
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
            _overlayStates.Clear();
            _confirmedNoIcons.Clear();
        }

        // ── HELPERS ──────────────────────────────────────────────────────────

        private static IconOverlayState GetOrCreateOverlay(IntPtr ptr)
            => _overlayStates.GetOrAdd(ptr, _ => new IconOverlayState());

        private static string ProcessTextTags(string originalText, out List<IconTagInfo> tagInfos)
        {
            tagInfos = null;
            if (string.IsNullOrEmpty(originalText)) return originalText;

            // [OPT-PERF] Use the pre-compiled static Regex — avoids building a new NFA per call.
            var matches = IcTagRegex.Matches(originalText);
            if (matches.Count == 0) return originalText;

            string cleanedText = originalText;
            int offset = 0;

            foreach (Match match in matches)
            {
                string tagName = match.Groups[1].Value;

                var sprite = IconsConfigLoader.GetSprite(tagName);
                if (sprite == null) continue;

                int cleanedIndex = match.Index - offset;
                const string spacer = "    "; // 4 spaces
                cleanedText = cleanedText.Remove(cleanedIndex, match.Length).Insert(cleanedIndex, spacer);

                if (tagInfos == null) tagInfos = new List<IconTagInfo>();
                tagInfos.Add(new IconTagInfo { TagName = tagName, CharIndex = cleanedIndex });

                offset += (match.Length - spacer.Length);
            }

            return cleanedText;
        }

        private static void DrawIcons(Text textComp)
        {
            if (textComp == null) return;

            var ptr = textComp.Pointer;

            // [OPT-PERF] Fast-exit for confirmed-no-icons text.
            if (_confirmedNoIcons.ContainsKey(ptr)) return;

            if (!_overlayStates.TryGetValue(ptr, out var state) || state.Tags == null || state.Tags.Count == 0)
            {
                // If there is overlay state (meaning slots were previously created), hide them.
                if (state != null && state.Slots.Count > 0)
                {
                    foreach (var slot in state.Slots)
                    {
                        if (slot.Go != null) slot.Go.SetActive(false);
                    }
                }
                return;
            }

            int characterCount = textComp.cachedTextGenerator.characterCount;

            for (int i = 0; i < state.Tags.Count; i++)
            {
                var tagInfo = state.Tags[i];
                int targetIndex = tagInfo.CharIndex;

                if (targetIndex < 0 || targetIndex >= characterCount) continue;

                var charInfo = textComp.cachedTextGenerator.characters[targetIndex];
                Vector2 localPos = charInfo.cursorPos;

                // [OPT-PERF] Use cached slot instead of transform.Find + GetComponent every frame.
                Image img;
                RectTransform rect;
                if (i < state.Slots.Count)
                {
                    var slot = state.Slots[i];
                    if (slot.Go == null)
                    {
                        // Slot was destroyed (scene reload edge-case) — recreate.
                        (img, rect) = CreateIconSlot(textComp, i);
                        state.Slots[i] = (img.gameObject, img, rect);
                    }
                    else
                    {
                        slot.Go.SetActive(true);
                        img = slot.Img;
                        rect = slot.Rect;
                    }
                }
                else
                {
                    // First time we need this slot — create and cache it.
                    (img, rect) = CreateIconSlot(textComp, i);
                    state.Slots.Add((img.gameObject, img, rect));
                }

                img.sprite = IconsConfigLoader.GetSprite(tagInfo.TagName);
                img.color = Color.white;

                var parentPivot = textComp.rectTransform.pivot;
                rect.anchorMin = parentPivot;
                rect.anchorMax = parentPivot;
                rect.pivot = new Vector2(0f, 0.5f);
                float referenceFontSize = textComp.fontSize > 0 ? textComp.fontSize : 12f;
                rect.sizeDelta = new Vector2(referenceFontSize, referenceFontSize);
                rect.localScale = Vector3.one;
                rect.anchoredPosition = new Vector2(localPos.x, localPos.y - referenceFontSize * 0.5f);
            }

            // Hide any extra cached slots beyond the current tag count.
            for (int i = state.Tags.Count; i < state.Slots.Count; i++)
            {
                if (state.Slots[i].Go != null) state.Slots[i].Go.SetActive(false);
            }
        }

        private static (Image img, RectTransform rect) CreateIconSlot(Text textComp, int index)
        {
            var go = new GameObject($"__KupoIcon_{index}");
            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            go.transform.SetParent(textComp.transform, false);
            var rect = go.GetComponent<RectTransform>();
            return (img, rect);
        }
    }
}
