using System;
using System.Collections.Concurrent;
using HarmonyLib;
using Last.Management;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch]
    internal static class TextLoggingPatch
    {
        private static readonly ConcurrentDictionary<IntPtr, string> _lastLoggedValues = new();
        private static readonly ConcurrentDictionary<string, string> _pathLoggedValues = new(StringComparer.Ordinal);
        internal static readonly ConcurrentDictionary<string, string> ValueToKeyMap = new(StringComparer.Ordinal);

        internal static bool IsLogged(string path, string value)
        {
            if (_pathLoggedValues.TryGetValue(path, out var lastValue) && lastValue == value)
            {
                return true;
            }
            _pathLoggedValues[path] = value;
            return false;
        }

        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        private static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (__instance == null || string.IsNullOrEmpty(value)) return;
            LogTextFromComponent(__instance, value);
        }

        [HarmonyPatch(typeof(Text), "OnPopulateMesh", new[] { typeof(VertexHelper) })]
        [HarmonyPostfix]
        private static void OnPopulateMeshPostfix(Text __instance)
        {
            if (__instance == null) return;
            LogTextFromComponent(__instance, __instance.text);
        }

        private static void LogTextFromComponent(Text __instance, string value)
        {
            if (!KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value)
            {
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            // Deduplicate: only log if the text value for this specific Text instance has changed
            if (_lastLoggedValues.TryGetValue(__instance.Pointer, out var lastValue) && lastValue == value)
            {
                return;
            }

            _lastLoggedValues[__instance.Pointer] = value;

            // Generate full hierarchy path of the GameObject
            string path = GetGameObjectPath(__instance.gameObject);

            // Skip logging dialogue text components here since they are logged by MessageSpeakerPrefixPatch
            if (path.Contains("message_window(Clone)/root/message_parent") || path.Contains("spekerText"))
            {
                return;
            }

            // Find matching localization key if available
            string key = "None";
            if (ValueToKeyMap.TryGetValue(value, out var resolvedKey))
            {
                key = resolvedKey;
            }
            
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Key: '{key}' | Value: '{value}'");
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return string.Empty;
            
            string path = obj.name;
            Transform current = obj.transform;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }
    }

    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.GetMessage), new[] { typeof(string), typeof(bool) })]
    internal static class MessageManagerGetMessagePatch
    {
        [HarmonyPostfix]
        private static void GetMessagePostfix(string key, bool isReplace, string __result)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(__result))
            {
                return;
            }

            // Keep track of resolved values to map them back to their keys
            TextLoggingPatch.ValueToKeyMap[__result] = key;

            // Also map the clean text value (without any <IC_*> tags) so name fields with stripped tags can still resolve their key!
            string cleanVal = System.Text.RegularExpressions.Regex.Replace(__result, @"<IC_[A-Za-z0-9_]+>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(cleanVal))
            {
                TextLoggingPatch.ValueToKeyMap[cleanVal] = key;
            }
        }
    }
}
