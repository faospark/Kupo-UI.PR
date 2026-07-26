using System;
using System.Collections.Concurrent;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
    internal static class TextLoggingPatch
    {
        private static readonly ConcurrentDictionary<IntPtr, string> _lastLoggedValues = new();

        [HarmonyPrefix]
        private static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (!KupoUIPRPlugin.DiagnosticLogAllTextsConfig.Value)
            {
                return;
            }

            if (__instance == null || string.IsNullOrEmpty(value))
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
            if (path.Contains("message_window") || path.Contains("last_text") || path.Contains("spekerText"))
            {
                return;
            }
            
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextLog] Path: '{path}' | Value: '{value}'");
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
}
