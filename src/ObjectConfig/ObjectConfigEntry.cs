namespace KupoUI.PR.ObjectConfig;

/// <summary>
/// Represents a single object-manipulation rule loaded from an <c>ObjectConfig.json</c> file.
/// Only fields that are present in the JSON are applied; absent fields leave the object unchanged.
/// </summary>
internal sealed class ObjectConfigEntry
{
    /// <summary>
    /// The exact <see cref="UnityEngine.GameObject"/> name to match (required).
    /// </summary>
    internal string TargetObjectName { get; set; }

    /// <summary>
    /// Optional hierarchy path suffix used to disambiguate objects with the same name.
    /// Uses forward-slash notation, e.g. <c>Canvas/aspect_parent/menu_base(Clone)</c>.
    /// When omitted, name-only matching is used.
    /// </summary>
    internal string TargetPath { get; set; }

    /// <summary>
    /// Optional scene name filter. When set, the rule is only applied while that scene is active.
    /// When omitted, the rule applies in every scene.
    /// </summary>
    internal string SceneName { get; set; }

    /// <summary>
    /// When present, sets <c>transform.localPosition</c>.
    /// </summary>
    internal Vec3? Position { get; set; }

    /// <summary>
    /// When present, sets <c>transform.localEulerAngles</c>.
    /// </summary>
    internal Vec3? Rotation { get; set; }

    /// <summary>
    /// When present, sets <c>transform.localScale</c>.
    /// </summary>
    internal Vec3? Scale { get; set; }

    /// <summary>
    /// When present, calls <c>gameObject.SetActive(value)</c>.
    /// Note: Setting this to <c>false</c> inside a <c>SetActive(true)</c> postfix will
    /// immediately deactivate the object again.
    /// </summary>
    internal bool? SetActive { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// sets <c>Text.alignment</c> to the specified <see cref="UnityEngine.TextAnchor"/> value.
    /// Accepted values (case-insensitive): UpperLeft, UpperCenter, UpperRight,
    /// MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight.
    /// </summary>
    internal string TextAlignment { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// sets <c>Text.fontSize</c> to the specified value.
    /// </summary>
    internal int? FontSize { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// sets <c>Text.resizeTextForBestFit</c> to the specified value.
    /// </summary>
    internal bool? ResizeTextForBestFit { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// sets <c>Text.resizeTextMaxSize</c> to the specified value.
    /// </summary>
    internal int? ResizeTextMaxSize { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// sets <c>Text.resizeTextMinSize</c> to the specified value.
    /// </summary>
    internal int? ResizeTextMinSize { get; set; }

    /// <summary>
    /// When present and the GameObject has a <c>UnityEngine.UI.Text</c> component,
    /// forces its color to white.
    /// </summary>
    internal bool? TextColorWhite { get; set; }

    /// <summary>
    /// When <c>true</c> and the GameObject has one or more
    /// <c>UnityEngine.UI.Shadow</c> components, disables them all.
    /// </summary>
    internal bool? DisableShadow { get; set; }

    /// <summary>
    /// The disk path of the <c>ObjectConfig.json</c> file this entry was loaded from.
    /// Used for log messages.
    /// </summary>
    internal string SourceFile { get; set; }
}

/// <summary>A lightweight three-component float vector used by <see cref="ObjectConfigEntry"/>.</summary>
internal struct Vec3
{
    internal float X { get; set; }
    internal float Y { get; set; }
    internal float Z { get; set; }
}
