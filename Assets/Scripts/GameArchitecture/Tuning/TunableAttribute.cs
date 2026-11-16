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
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TunableAttribute : Attribute
    {
        /// <summary>Name of the tab the field appears in.</summary>
        public string Tab { get; }

        /// <summary>Folders inside the tab, separated by '/'. Empty = top level of the tab.</summary>
        public string Path { get; }

        public TunableAttribute(string tab, string path = "")
        {
            Tab = string.IsNullOrWhiteSpace(tab) ? "General" : tab;
            Path = path ?? "";
        }
    }
}
