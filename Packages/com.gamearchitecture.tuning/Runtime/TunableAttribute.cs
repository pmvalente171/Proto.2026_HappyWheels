using System;

namespace GameArchitecture.Tuning
{
    /// <summary>
    /// Shows a serialized field in the Tuning window (Window > Tuning), where it can be edited during play and saved.
    /// Works on fields of ScriptableObjects, including fields inside [Serializable] classes they contain.
    /// </summary>
    /// <example>
    /// <code>[Tunable("Bike", "Drivetrain/Boost")] public float boost = 1.5f;</code>
    /// Puts the field in the "Bike" tab, inside a "Drivetrain" folder, inside a "Boost" folder.
    /// <code>[Tunable("Bike", "Drivetrain", ReadOnly = true)] public float[] preview;</code>
    /// Shows the field but doesn't let it be edited (for values worked out by code, e.g. in OnValidate).
    /// </example>
    /// <remarks>Arrays and lists are drawn as tables, one row per element and one column per field.</remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TunableAttribute : Attribute
    {
        /// <summary>Name of the tab the field appears in.</summary>
        public string Tab { get; }

        /// <summary>Folders inside the tab, separated by '/'. Empty = top level of the tab.</summary>
        public string Path { get; }

        /// <summary>Shown but not editable, in both the Tuning window and the Inspector.</summary>
        public bool ReadOnly { get; set; }

        public TunableAttribute(string tab, string path = "")
        {
            Tab = string.IsNullOrWhiteSpace(tab) ? "General" : tab;
            Path = path ?? "";
        }
    }
}
