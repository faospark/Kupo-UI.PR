using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Last.Data.Master;

namespace KupoUI.PR.Patches
{
    [HarmonyPatch]
    internal static class DatabaseConfigPatch
    {
        private static readonly Dictionary<int, DatabaseConfig.DatabaseConfigEntry> _partyOverrides = new();

        internal static void Initialize(string modulesRootPath, Harmony harmony)
        {
            DatabaseConfig.DatabaseConfigLoader.Load(modulesRootPath);

            var entries = DatabaseConfig.DatabaseConfigLoader.Entries;
            _partyOverrides.Clear();
            foreach (var entry in entries)
            {
                _partyOverrides[entry.Id] = entry;
            }

            if (_partyOverrides.Count > 0)
            {
                KupoUIPRPlugin.PluginLog.LogInfo($"[DatabaseConfig] Loaded {_partyOverrides.Count} monster party override(s). Initializing Harmony patch.");
                ApplyPatch(harmony);
            }
        }

        private static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var masterBaseType = AccessTools.TypeByName("Last.Data.Master.MasterBase");
                if (masterBaseType == null)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning("[DatabaseConfig] Could not find type Last.Data.Master.MasterBase");
                    return;
                }

                // Find static Dictionary<int, T> Parse<T>(string csvText)
                var parseMethod = AccessTools.Method(masterBaseType, "Parse", new[] { typeof(string) });
                if (parseMethod == null)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning("[DatabaseConfig] Could not find MasterBase.Parse method");
                    return;
                }

                var monsterPartyType = AccessTools.TypeByName("Last.Data.Master.MonsterParty");
                if (monsterPartyType == null)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning("[DatabaseConfig] Could not find type Last.Data.Master.MonsterParty");
                    return;
                }

                // Make generic Parse<MonsterParty>
                var concreteParseMethod = parseMethod.MakeGenericMethod(monsterPartyType);
                if (concreteParseMethod == null)
                {
                    KupoUIPRPlugin.PluginLog.LogWarning("[DatabaseConfig] Could not make concrete generic method Parse<MonsterParty>");
                    return;
                }

                var postfixMethod = new HarmonyMethod(typeof(DatabaseConfigPatch), nameof(ParseMonsterPartyPostfix));
                harmony.Patch(concreteParseMethod, postfix: postfixMethod);
                KupoUIPRPlugin.PluginLog.LogInfo("[DatabaseConfig] Successfully patched MasterBase.Parse<MonsterParty>!");
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[DatabaseConfig] Failed to apply database override patch: {ex}");
            }
        }

        private static void ParseMonsterPartyPostfix(Il2CppSystem.Collections.Generic.Dictionary<int, Last.Data.Master.MonsterParty> __result)
        {
            if (__result == null) return;

            try
            {
                var totalApplied = 0;
                foreach (var kvp in _partyOverrides)
                {
                    int partyId = kvp.Key;
                    var o = kvp.Value;

                    if (__result.ContainsKey(partyId))
                    {
                        var party = __result[partyId];
                        if (party != null)
                        {
                            ApplyOverrides(party, o);
                            totalApplied++;
                        }
                    }
                }

                if (totalApplied > 0)
                {
                    KupoUIPRPlugin.PluginLog.LogInfo($"[DatabaseConfig] Successfully applied {totalApplied} monster party overrides in memory.");
                }
            }
            catch (Exception ex)
            {
                KupoUIPRPlugin.PluginLog.LogError($"[DatabaseConfig] Error applying overrides in ParseMonsterPartyPostfix: {ex}");
            }
        }

        private static void ApplyOverrides(Last.Data.Master.MonsterParty party, DatabaseConfig.DatabaseConfigEntry o)
        {
            if (o.BattleBackgroundAssetId.HasValue) party.BattleBackgroundAssetId = o.BattleBackgroundAssetId.Value;
            if (o.BattleBgmAssetId.HasValue) party.BattleBgmAssetId = o.BattleBgmAssetId.Value;
            if (o.AppearanceProduction.HasValue) party.AppearanceProduction = o.AppearanceProduction.Value;
            if (o.ScriptNameId.HasValue) party.ScriptNameId = o.ScriptNameId.Value;
            if (o.BattlePattern1.HasValue) party.BattlePattern1 = o.BattlePattern1.Value;
            if (o.BattlePattern2.HasValue) party.BattlePattern2 = o.BattlePattern2.Value;
            if (o.BattlePattern3.HasValue) party.BattlePattern3 = o.BattlePattern3.Value;
            if (o.BattlePattern4.HasValue) party.BattlePattern4 = o.BattlePattern4.Value;
            if (o.BattlePattern5.HasValue) party.BattlePattern5 = o.BattlePattern5.Value;
            if (o.BattlePattern6.HasValue) party.BattlePattern6 = o.BattlePattern6.Value;
            if (o.NotEscape.HasValue) party.NotEscape = o.NotEscape.Value;
            if (o.BattleFlagGroupId.HasValue) party.BattleFlagGroupId = o.BattleFlagGroupId.Value;
            if (o.GetValue.HasValue) party.GetValue = o.GetValue.Value;
            if (o.GetAp.HasValue) party.GetAp = o.GetAp.Value;

            if (o.Monster1.HasValue) party.Monster1 = o.Monster1.Value;
            if (o.Monster1XPosition.HasValue) party.Monster1XPosition = o.Monster1XPosition.Value;
            if (o.Monster1YPosition.HasValue) party.Monster1YPosition = o.Monster1YPosition.Value;
            if (o.Monster1Group.HasValue) party.Monster1Group = o.Monster1Group.Value;

            if (o.Monster2.HasValue) party.Monster2 = o.Monster2.Value;
            if (o.Monster2XPosition.HasValue) party.Monster2XPosition = o.Monster2XPosition.Value;
            if (o.Monster2YPosition.HasValue) party.Monster2YPosition = o.Monster2YPosition.Value;
            if (o.Monster2Group.HasValue) party.Monster2Group = o.Monster2Group.Value;

            if (o.Monster3.HasValue) party.Monster3 = o.Monster3.Value;
            if (o.Monster3XPosition.HasValue) party.Monster3XPosition = o.Monster3XPosition.Value;
            if (o.Monster3YPosition.HasValue) party.Monster3YPosition = o.Monster3YPosition.Value;
            if (o.Monster3Group.HasValue) party.Monster3Group = o.Monster3Group.Value;

            if (o.Monster4.HasValue) party.Monster4 = o.Monster4.Value;
            if (o.Monster4XPosition.HasValue) party.Monster4XPosition = o.Monster4XPosition.Value;
            if (o.Monster4YPosition.HasValue) party.Monster4YPosition = o.Monster4YPosition.Value;
            if (o.Monster4Group.HasValue) party.Monster4Group = o.Monster4Group.Value;

            if (o.Monster5.HasValue) party.Monster5 = o.Monster5.Value;
            if (o.Monster5XPosition.HasValue) party.Monster5XPosition = o.Monster5XPosition.Value;
            if (o.Monster5YPosition.HasValue) party.Monster5YPosition = o.Monster5YPosition.Value;
            if (o.Monster5Group.HasValue) party.Monster5Group = o.Monster5Group.Value;

            if (o.Monster6.HasValue) party.Monster6 = o.Monster6.Value;
            if (o.Monster6XPosition.HasValue) party.Monster6XPosition = o.Monster6XPosition.Value;
            if (o.Monster6YPosition.HasValue) party.Monster6YPosition = o.Monster6YPosition.Value;
            if (o.Monster6Group.HasValue) party.Monster6Group = o.Monster6Group.Value;

            if (o.Monster7.HasValue) party.Monster7 = o.Monster7.Value;
            if (o.Monster7XPosition.HasValue) party.Monster7XPosition = o.Monster7XPosition.Value;
            if (o.Monster7YPosition.HasValue) party.Monster7YPosition = o.Monster7YPosition.Value;
            if (o.Monster7Group.HasValue) party.Monster7Group = o.Monster7Group.Value;

            if (o.Monster8.HasValue) party.Monster8 = o.Monster8.Value;
            if (o.Monster8XPosition.HasValue) party.Monster8XPosition = o.Monster8XPosition.Value;
            if (o.Monster8YPosition.HasValue) party.Monster8YPosition = o.Monster8YPosition.Value;
            if (o.Monster8Group.HasValue) party.Monster8Group = o.Monster8Group.Value;

            if (o.Monster9.HasValue) party.Monster9 = o.Monster9.Value;
            if (o.Monster9XPosition.HasValue) party.Monster9XPosition = o.Monster9XPosition.Value;
            if (o.Monster9YPosition.HasValue) party.Monster9YPosition = o.Monster9YPosition.Value;
            if (o.Monster9Group.HasValue) party.Monster9Group = o.Monster9Group.Value;
        }
    }
}
