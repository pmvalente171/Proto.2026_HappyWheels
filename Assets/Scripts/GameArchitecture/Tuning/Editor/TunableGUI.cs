using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameArchitecture.Tuning.Editing
{
    /// <summary>
    /// Drawing shared by the Tuning window and the Inspector: arrays and lists as tables (one row per element,
    /// one column per field), and read-only fields.
    /// Column headers use the element field's [InspectorName] if it has one, otherwise its nicified name; [Tooltip]s show on hover.
    /// </summary>
    public static class TunableGUI
    {
        private const float IndexWidth = 24f;
        private const float ButtonWidth = 20f;
        private const float Gap = 4f;
        private static readonly Color StripeColor = new(0.5f, 0.5f, 0.5f, 0.08f);
        private static readonly Color HeaderLineColor = new(0.5f, 0.5f, 0.5f, 0.35f);

        private readonly struct Column
        {
            public readonly string Field; // null = the element itself (lists of plain values)
            public readonly GUIContent Header;

            public Column(string field, GUIContent header)
            {
                Field = field;
                Header = header;
            }
        }

        private static readonly Dictionary<Type, Column[]> ColumnCache = new();

        public static bool IsTable(SerializedProperty property) =>
            property.isArray && property.propertyType != SerializedPropertyType.String;

        /// <summary>Draws a property with its label: arrays and lists as tables, greyed out when read-only.</summary>
        public static void PropertyField(SerializedProperty property, bool readOnly)
        {
            if (IsTable(property))
            {
                DrawDecorators(property);
                Table(property, readOnly);
                return;
            }

            using (new EditorGUI.DisabledScope(readOnly))
                EditorGUILayout.PropertyField(property, true);
        }

        /// <summary>Draws the [Header] and [Space] attributes of a property that isn't drawn with PropertyField.</summary>
        public static void DrawDecorators(SerializedProperty property)
        {
            FieldInfo field = TunableFields.GetField(property.serializedObject.targetObject.GetType(), property.propertyPath);
            if (field == null)
                return;

            foreach (PropertyAttribute attribute in field.GetCustomAttributes<PropertyAttribute>(true).OrderBy(a => a.order))
            {
                switch (attribute)
                {
                    case SpaceAttribute space:
                        EditorGUILayout.Space(space.height);
                        break;
                    case HeaderAttribute header:
                        EditorGUILayout.Space(EditorGUIUtility.singleLineHeight * 0.5f);
                        EditorGUILayout.LabelField(header.header, EditorStyles.boldLabel);
                        break;
                }
            }
        }

        /// <summary>Draws an array or list as a foldout table.</summary>
        public static void Table(SerializedProperty array, bool readOnly)
        {
            array.isExpanded = EditorGUILayout.Foldout(array.isExpanded,
                new GUIContent($"{array.displayName}  ({array.arraySize})", array.tooltip), true);
            if (!array.isExpanded)
                return;

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = indent + 1;
            try
            {
                Column[] columns = GetColumns(array);
                bool showIndex = columns.Length == 1 && columns[0].Field == null;
                bool editable = !readOnly;

                // Header
                Rect headerRow = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                EditorGUI.indentLevel = 0;
                Layout(headerRow, columns.Length, showIndex, editable, out Rect indexCell, out Rect[] cells, out _);
                if (showIndex)
                    EditorGUI.LabelField(indexCell, "#", EditorStyles.miniBoldLabel);
                for (int c = 0; c < columns.Length; c++)
                    EditorGUI.LabelField(cells[c], columns[c].Header, EditorStyles.miniBoldLabel);
                EditorGUI.DrawRect(new Rect(headerRow.x, headerRow.yMax, headerRow.width, 1f), HeaderLineColor);
                EditorGUI.indentLevel = indent + 1;

                // Rows
                int removeAt = -1;
                for (int i = 0; i < array.arraySize; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    Rect row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                    EditorGUI.indentLevel = 0;

                    if (i % 2 == 1)
                        EditorGUI.DrawRect(new Rect(row.x - 2f, row.y - 1f, row.width + 4f, row.height + 2f), StripeColor);

                    Layout(row, columns.Length, showIndex, editable, out indexCell, out cells, out Rect buttonCell);
                    if (showIndex)
                        EditorGUI.LabelField(indexCell, i.ToString(), EditorStyles.miniLabel);

                    for (int c = 0; c < columns.Length; c++)
                    {
                        SerializedProperty cell = columns[c].Field == null ? element : element.FindPropertyRelative(columns[c].Field);
                        if (cell != null)
                            DrawCell(cells[c], cell, readOnly);
                    }

                    if (editable && GUI.Button(buttonCell, new GUIContent("−", "Remove this row"), EditorStyles.miniButton))
                        removeAt = i;

                    EditorGUI.indentLevel = indent + 1;
                }

                if (removeAt >= 0)
                    array.DeleteArrayElementAtIndex(removeAt);

                // Footer
                if (editable || array.arraySize == 0)
                {
                    Rect footer = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                    EditorGUI.indentLevel = 0;
                    if (array.arraySize == 0)
                        EditorGUI.LabelField(footer, "Empty", EditorStyles.centeredGreyMiniLabel);
                    if (editable && GUI.Button(new Rect(footer.xMax - ButtonWidth, footer.y, ButtonWidth, footer.height),
                            new GUIContent("+", "Add a row"), EditorStyles.miniButton))
                        array.arraySize++;
                }
            }
            finally
            {
                EditorGUI.indentLevel = indent;
            }
        }

        private static void Layout(Rect row, int columnCount, bool showIndex, bool editable,
            out Rect indexCell, out Rect[] cells, out Rect buttonCell)
        {
            float x = row.x;
            float right = row.xMax;

            indexCell = new Rect(x, row.y, IndexWidth, row.height);
            if (showIndex)
                x += IndexWidth + Gap;

            buttonCell = new Rect(right - ButtonWidth, row.y, ButtonWidth, row.height);
            if (editable)
                right -= ButtonWidth + Gap;

            cells = new Rect[columnCount];
            float width = Mathf.Max((right - x - Gap * (columnCount - 1)) / Mathf.Max(columnCount, 1), 10f);
            for (int c = 0; c < columnCount; c++)
                cells[c] = new Rect(x + c * (width + Gap), row.y, width, row.height);
        }

        private static void DrawCell(Rect rect, SerializedProperty cell, bool readOnly)
        {
            if (readOnly)
            {
                // Plain text reads better than greyed-out fields for values you can't edit
                string text = FormatValue(cell);
                if (text != null)
                {
                    EditorGUI.LabelField(rect, text);
                    return;
                }
            }

            using (new EditorGUI.DisabledScope(readOnly))
                EditorGUI.PropertyField(rect, cell, GUIContent.none, false);
        }

        private static string FormatValue(SerializedProperty property) => property.propertyType switch
        {
            SerializedPropertyType.Integer => property.intValue.ToString(),
            SerializedPropertyType.Float => property.floatValue.ToString("0.##"),
            SerializedPropertyType.Boolean => property.boolValue ? "Yes" : "No",
            SerializedPropertyType.String => property.stringValue,
            SerializedPropertyType.Enum when property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                => property.enumDisplayNames[property.enumValueIndex],
            _ => null,
        };

        private static Column[] GetColumns(SerializedProperty array)
        {
            FieldInfo field = TunableFields.GetField(array.serializedObject.targetObject.GetType(), array.propertyPath);
            Type elementType = field != null ? TunableFields.ElementType(field.FieldType) : null;
            if (elementType == null || !TunableFields.IsNestedSerializable(elementType))
                return new[] { new Column(null, new GUIContent("Value")) };

            if (!ColumnCache.TryGetValue(elementType, out Column[] columns))
            {
                columns = TunableFields.SerializedFields(elementType)
                    .Where(f => !f.IsDefined(typeof(HideInInspector), true))
                    .Select(f => new Column(f.Name, new GUIContent(
                        f.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? ObjectNames.NicifyVariableName(f.Name),
                        f.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "")))
                    .ToArray();
                ColumnCache[elementType] = columns;
            }
            return columns;
        }
    }
}
