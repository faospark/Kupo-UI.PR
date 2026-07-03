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
        "Memoria FF VI PR"
    };

    private static readonly string[] MagiciteAssemblyNames =
    {
        MagiciteAssemblyName,
        "Magicite Loader"
    };

    public static bool IsMemoriaLoaded => TryGetAssembly(MemoriaAssemblyNames, out _);
    public static bool IsMagiciteLoaded => TryGetAssembly(MagiciteAssemblyNames, out _);

    public static void LogLoadedOptionalMods(ManualLogSource log)
    {
        if (TryGetAssembly(MemoriaAssemblyNames, out var memoriaAssembly))
        {
            log.LogInfo($"Optional dependency detected: {memoriaAssembly.GetName().Name} ({memoriaAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo($"Optional dependency not found: {string.Join(", ", MemoriaAssemblyNames)}");
        }

        if (TryGetAssembly(MagiciteAssemblyNames, out var magiciteAssembly))
        {
            log.LogInfo($"Optional dependency detected: {magiciteAssembly.GetName().Name} ({magiciteAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo($"Optional dependency not found: {string.Join(", ", MagiciteAssemblyNames)}");
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
