namespace KupoUI.PR.TextConfig
{
    /// <summary>
    /// Represents a single text override rule loaded from a <c>TextConfig.json</c> file.
    /// Supports GameObject matching (TargetObjectName/TargetPath/SceneName),
    /// localization key matching (Key), or direct string matching (OriginalText).
    /// </summary>
    internal sealed class TextConfigEntry
    {
        /// <summary>
        /// Optional exact <see cref="UnityEngine.GameObject"/> name to match.
        /// </summary>
        internal string TargetObjectName { get; set; }

        /// <summary>
        /// Optional hierarchy path suffix to disambiguate objects with the same name.
        /// </summary>
        internal string TargetPath { get; set; }

        /// <summary>
        /// Optional scene name filter.
        /// </summary>
        internal string SceneName { get; set; }

        /// <summary>
        /// Optional localization key (e.g. "MSG_SYSTEM_022") to match during string lookup.
        /// </summary>
        internal string Key { get; set; }

        /// <summary>
        /// Optional original text value to match.
        /// </summary>
        internal string OriginalText { get; set; }

        /// <summary>
        /// Optional language filter (e.g. "En", "Ja", "Fr", etc.) to match.
        /// </summary>
        internal string Language { get; set; }

        /// <summary>
        /// The new replacement text (required).
        /// </summary>
        internal string NewText { get; set; }

        /// <summary>
        /// The path of the <c>TextConfig.json</c> file this rule was loaded from.
        /// </summary>
        internal string SourceFile { get; set; }
    }
}
