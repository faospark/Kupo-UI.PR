using System;
using System.Collections.Generic;

namespace KupoUI.PR.Textures;

internal static class TextureLogger
{
    private static readonly HashSet<string> SeenTextures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ResolvedTextures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingTextures = new(StringComparer.OrdinalIgnoreCase);

    private static bool _enabled;
    private static bool _logDiscoveries;
    private static bool _logResolutions;
    private static bool _logMisses;

    internal static void Initialize(bool enabled, bool logDiscoveries, bool logResolutions, bool logMisses)
    {
        _enabled = enabled;
        _logDiscoveries = logDiscoveries;
        _logResolutions = logResolutions;
        _logMisses = logMisses;

        SeenTextures.Clear();
        ResolvedTextures.Clear();
        MissingTextures.Clear();

        if (_enabled)
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureLogger] Enabled (Discoveries={_logDiscoveries}, Resolutions={_logResolutions}, Misses={_logMisses})");
        }
    }

    internal static void LogObservedTextureName(string textureName, string source)
    {
        if (!_enabled || !_logDiscoveries)
        {
            return;
        }

        var key = TextureResolver.NormalizeName(textureName);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (SeenTextures.Add(key))
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureLogger][Seen:{source}] {key}");
        }
    }

    internal static void LogResolvedTexture(string textureName, string sourcePath, string source)
    {
        if (!_enabled || !_logResolutions)
        {
            return;
        }

        var key = TextureResolver.NormalizeName(textureName);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (ResolvedTextures.Add(key))
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureLogger][Resolved:{source}] {key} <= {sourcePath}");
        }
    }

    internal static void LogMissingTexture(string textureName, string source)
    {
        if (!_enabled || !_logMisses)
        {
            return;
        }

        var key = TextureResolver.NormalizeName(textureName);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (MissingTextures.Add(key))
        {
            KupoUIPRPlugin.PluginLog.LogInfo($"[TextureLogger][Missing:{source}] {key}");
        }
    }
}
