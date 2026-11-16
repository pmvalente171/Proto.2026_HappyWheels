using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameArchitecture.Tuning.Editing
{
    /// <summary>
    /// Inspector for ScriptableObjects with [Tunable] fields, so they look the same as in the Tuning window:
    /// arrays and lists as tables, and Tunable(ReadOnly = true) fields not editable.
    /// ScriptableObjects without tunables, or with their own custom editor, are untouched.
    /// </summary>
    [CustomEditor(typeof(ScriptableObject), true, isFallback = true)]
    [CanEditMultipleObjects]
    internal class TunableAssetEditor : UnityEditor.Editor
    {
        private IReadOnlyDictionary<string, TunableAttribute> _tunables;

        private bool HasTunables => _tunables is { Count: > 0 };

        private void OnEnable() => _tunables = target ? TunableFields.For(target.GetType()) : null;

        public override VisualElement CreateInspectorGUI()
        {
            if (HasTunables)
                return new IMGUIContainer(OnInspectorGUI);

            // Unity's normal inspector
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }

        public override void OnInspectorGUI()
        {
            if (!HasTunables)
            {
                base.OnInspectorGUI();
                return;
            }

            serializedObject.Update();
            SerializedProperty iterator = serializedObject.GetIterator();
            for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
            {
                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator);
                    continue;
                }
                Draw(iterator.Copy());
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void Draw(SerializedProperty property)
        {
            if (_tunables.TryGetValue(property.propertyPath, out TunableAttribute tunable))
            {
                TunableGUI.PropertyField(property, tunable.ReadOnly);
                return;
            }

            if (TunableGUI.IsTable(property))
            {
                TunableGUI.PropertyField(property, false);
                return;
            }

            // Nested class with tunables inside: draw its children ourselves so they get the same treatment
            if (property.propertyType == SerializedPropertyType.Generic && HasTunablesUnder(property.propertyPath))
            {
                TunableGUI.DrawDecorators(property);
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded,
                    new GUIContent(property.displayName, property.tooltip), true);
                if (!property.isExpanded)
                    return;

                EditorGUI.indentLevel++;
                SerializedProperty end = property.GetEndProperty();
                SerializedProperty child = property.Copy();
                for (bool enterChildren = true;
                     child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end);
                     enterChildren = false)
                    Draw(child.Copy());
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.PropertyField(property, true);
        }

        private bool HasTunablesUnder(string propertyPath)
        {
            string prefix = propertyPath + ".";
            return _tunables.Keys.Any(k => k.StartsWith(prefix));
        }
    }
}
