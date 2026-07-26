using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace KupoUI.PR.Compatibility;

internal static class ExternalModDetector
{
    private const string MemoriaAssemblyName = "Memoria.FFPR";
    private const string MagiciteAssemblyName = "Magicite";
    private static readonly string[] MemoriaAssemblyNames =
    {
        MemoriaAssemblyName,
        "Memoria FF PR",
        "Memoria FF II PR",
        "Memoria FF III PR",
        "Memoria FF IV PR",
        "Memoria FF V PR",
        "Memoria FF VI PR",
        "Memoria.FF1",
        "Memoria.FF2",
        "Memoria.FF3",
        "Memoria.FF4",
        "Memoria.FF5",
        "Memoria.FF6"
    };

    private static readonly string[] MagiciteAssemblyNames =
    {
        MagiciteAssemblyName,
        "Magicite Loader"
    };

    private const string FfprFixAssemblyName = "FFPR_Fix";
    private static readonly string[] FfprFixAssemblyNames =
    {
        FfprFixAssemblyName,
        "FFPR-Fix",
        "d3xMachina.ffpr_fix",
        "ffpr_fix"
    };

    public static bool IsMemoriaLoaded => TryGetAssembly(MemoriaAssemblyNames, out _);
    public static bool IsMagiciteLoaded => TryGetAssembly(MagiciteAssemblyNames, out _);
    public static bool IsFfprFixLoaded => TryGetAssembly(FfprFixAssemblyNames, out _);

    private static bool _hasLogged = false;

    public static void LogLoadedOptionalMods(ManualLogSource log)
    {
        if (_hasLogged) return;
        _hasLogged = true;

        if (TryGetAssembly(MemoriaAssemblyNames, out var memoriaAssembly))
        {
            log.LogInfo($"Optional dependency detected: {memoriaAssembly.GetName().Name} ({memoriaAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo("Optional dependency not found: Memoria");
            try
            {
                var matching = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .Where(name => !string.IsNullOrEmpty(name) && name.IndexOf("Memoria", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (matching.Count > 0)
                {
                    log.LogInfo($"[Diagnostics] Found assemblies containing 'Memoria': {string.Join(", ", matching)}");
                }
                else
                {
                    log.LogInfo("[Diagnostics] No loaded assemblies contain 'Memoria'.");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"[Diagnostics] Failed to list assemblies containing 'Memoria': {ex.Message}");
            }
        }

        if (TryGetAssembly(MagiciteAssemblyNames, out var magiciteAssembly))
        {
            log.LogInfo($"Optional dependency detected: {magiciteAssembly.GetName().Name} ({magiciteAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo("Optional dependency not found: Magicite");
            try
            {
                var matching = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .Where(name => !string.IsNullOrEmpty(name) && name.IndexOf("Magicite", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (matching.Count > 0)
                {
                    log.LogInfo($"[Diagnostics] Found assemblies containing 'Magicite': {string.Join(", ", matching)}");
                }
                else
                {
                    log.LogInfo("[Diagnostics] No loaded assemblies contain 'Magicite'.");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"[Diagnostics] Failed to list assemblies containing 'Magicite': {ex.Message}");
            }
        }

        if (TryGetAssembly(FfprFixAssemblyNames, out var ffprFixAssembly))
        {
            log.LogInfo($"Optional dependency detected: {ffprFixAssembly.GetName().Name} ({ffprFixAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo("Optional dependency not found: FFPR_Fix");
            try
            {
                var matching = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .Where(name => !string.IsNullOrEmpty(name) && name.IndexOf("Fix", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (matching.Count > 0)
                {
                    log.LogInfo($"[Diagnostics] Found assemblies containing 'Fix': {string.Join(", ", matching)}");
                }
                else
                {
                    log.LogInfo("[Diagnostics] No loaded assemblies contain 'Fix'.");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"[Diagnostics] Failed to list assemblies containing 'Fix': {ex.Message}");
            }
        }
    }

    private static bool TryGetAssembly(string[] simpleNames, out Assembly assembly)
    {
        foreach (var simpleName in simpleNames)
        {
            if (TryGetAssembly(simpleName, out assembly))
            {
                return true;
            }
        }

        assembly = null;
        return false;
    }

    private static bool TryGetAssembly(string simpleName, out Assembly assembly)
    {
        assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        return assembly != null;
    }
}
