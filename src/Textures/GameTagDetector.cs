using System;
using BepInEx;

namespace KupoUI.PR.Textures;

internal static class GameTagDetector
{
    internal static string Detect(string gameTagOverride)
    {
        if (!string.IsNullOrWhiteSpace(gameTagOverride))
        {
            return NormalizeTag(gameTagOverride);
        }

        var source = (Paths.GameRootPath ?? string.Empty).ToLowerInvariant();

        if (ContainsAny(source, "ff6", "final fantasy vi", "final fantasy 6", "pixel remaster vi"))
        {
            return "FF6";
        }

        if (ContainsAny(source, "ff5", "final fantasy v", "final fantasy 5", "pixel remaster v"))
        {
            return "FF5";
        }

        if (ContainsAny(source, "ff4", "final fantasy iv", "final fantasy 4", "pixel remaster iv"))
        {
            return "FF4";
        }

        if (ContainsAny(source, "ff3", "final fantasy iii", "final fantasy 3", "pixel remaster iii"))
        {
            return "FF3";
        }

        if (ContainsAny(source, "ff2", "final fantasy ii", "final fantasy 2", "pixel remaster ii"))
        {
            return "FF2";
        }

        if (ContainsAny(source, "ff1", "final fantasy", "final fantasy", "pixel remaster", "final fantasy"))
        {
            return "FF1";
        }

        return "Shared";
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (source.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTag(string rawTag)
    {
        var value = rawTag.Trim().ToUpperInvariant();
        if (value is "FF1" or "FF2" or "FF3" or "FF4" or "FF5" or "FF6" or "SHARED")
        {
            return value == "SHARED" ? "Shared" : value;
        }

        return "Shared";
    }
}
