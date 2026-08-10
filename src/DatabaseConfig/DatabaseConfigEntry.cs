using System;

namespace KupoUI.PR.DatabaseConfig
{
    internal sealed class DatabaseConfigEntry
    {
        public int Id { get; set; }
        public string SourceFile { get; set; }

        public int? BattleBackgroundAssetId { get; set; }
        public int? BattleBgmAssetId { get; set; }
        public int? AppearanceProduction { get; set; }
        public int? ScriptNameId { get; set; }
        public int? BattlePattern1 { get; set; }
        public int? BattlePattern2 { get; set; }
        public int? BattlePattern3 { get; set; }
        public int? BattlePattern4 { get; set; }
        public int? BattlePattern5 { get; set; }
        public int? BattlePattern6 { get; set; }
        public int? NotEscape { get; set; }
        public int? BattleFlagGroupId { get; set; }
        public int? GetValue { get; set; }
        public int? GetAp { get; set; }

        // Monster slots 1-9
        public int? Monster1 { get; set; }
        public int? Monster1XPosition { get; set; }
        public int? Monster1YPosition { get; set; }
        public int? Monster1Group { get; set; }

        public int? Monster2 { get; set; }
        public int? Monster2XPosition { get; set; }
        public int? Monster2YPosition { get; set; }
        public int? Monster2Group { get; set; }

        public int? Monster3 { get; set; }
        public int? Monster3XPosition { get; set; }
        public int? Monster3YPosition { get; set; }
        public int? Monster3Group { get; set; }

        public int? Monster4 { get; set; }
        public int? Monster4XPosition { get; set; }
        public int? Monster4YPosition { get; set; }
        public int? Monster4Group { get; set; }

        public int? Monster5 { get; set; }
        public int? Monster5XPosition { get; set; }
        public int? Monster5YPosition { get; set; }
        public int? Monster5Group { get; set; }

        public int? Monster6 { get; set; }
        public int? Monster6XPosition { get; set; }
        public int? Monster6YPosition { get; set; }
        public int? Monster6Group { get; set; }

        public int? Monster7 { get; set; }
        public int? Monster7XPosition { get; set; }
        public int? Monster7YPosition { get; set; }
        public int? Monster7Group { get; set; }

        public int? Monster8 { get; set; }
        public int? Monster8XPosition { get; set; }
        public int? Monster8YPosition { get; set; }
        public int? Monster8Group { get; set; }

        public int? Monster9 { get; set; }
        public int? Monster9XPosition { get; set; }
        public int? Monster9YPosition { get; set; }
        public int? Monster9Group { get; set; }
    }
}
