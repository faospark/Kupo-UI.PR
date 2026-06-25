using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace KupoUI.PR.Compatibility;

internal static class ExternalModDetector
{
    private const string MemoriaAssemblyName = "Memoria.FFPR";
    private const string MagiciteAssemblyName = "Magicite";

    public static bool IsMemoriaLoaded => TryGetAssembly(MemoriaAssemblyName, out _);
    public static bool IsMagiciteLoaded => TryGetAssembly(MagiciteAssemblyName, out _);

    public static void LogLoadedOptionalMods(ManualLogSource log)
    {
        if (TryGetAssembly(MemoriaAssemblyName, out var memoriaAssembly))
        {
            log.LogInfo($"Optional dependency detected: {MemoriaAssemblyName} ({memoriaAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo($"Optional dependency not found: {MemoriaAssemblyName}");
        }

        if (TryGetAssembly(MagiciteAssemblyName, out var magiciteAssembly))
        {
            log.LogInfo($"Optional dependency detected: {MagiciteAssemblyName} ({magiciteAssembly.GetName().Version})");
        }
        else
        {
            log.LogInfo($"Optional dependency not found: {MagiciteAssemblyName}");
        }
    }

    private static bool TryGetAssembly(string simpleName, out Assembly assembly)
    {
        assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        return assembly != null;
    }
}
