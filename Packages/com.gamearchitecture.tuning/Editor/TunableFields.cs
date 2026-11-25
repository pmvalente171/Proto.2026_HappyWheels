using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameArchitecture.Tuning.Editing
{
    /// <summary>Reflection helpers shared by the Tuning window and the Inspector: finds [Tunable] fields and field info for property paths.</summary>
    internal static class TunableFields
    {
        private const int MaxNestingDepth = 8;
        private const BindingFlags DeclaredFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, Dictionary<string, TunableAttribute>> Cache = new();

        /// <summary>Every [Tunable] field of a type, by property path (e.g. "drivetrain.gearCount").</summary>
        public static IReadOnlyDictionary<string, TunableAttribute> For(Type type)
        {
            if (!Cache.TryGetValue(type, out var map))
                Cache[type] = map = Collect(type).ToDictionary(t => t.propertyPath, t => t.attribute);
            return map;
        }

        /// <summary>Walks a type's serialized fields, yielding the property path of every [Tunable] one.</summary>
        public static IEnumerable<(string propertyPath, TunableAttribute attribute)> Collect(Type type, string prefix = "", int depth = 0)
        {
            if (depth > MaxNestingDepth)
                yield break;

            foreach (FieldInfo field in SerializedFields(type))
            {
                string path = prefix + field.Name;
                var attribute = field.GetCustomAttribute<TunableAttribute>();
                if (attribute != null)
                {
                    yield return (path, attribute);
                    continue;
                }

                if (IsNestedSerializable(field.FieldType))
                {
                    foreach (var nested in Collect(field.FieldType, path + ".", depth + 1))
                        yield return nested;
                }
            }
        }

        /// <summary>Serialized fields of a type in Inspector order (base classes first).</summary>
        public static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            var hierarchy = new List<Type>();
            for (Type t = type; t != null && t != typeof(ScriptableObject) && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
                hierarchy.Insert(0, t);
            return hierarchy.SelectMany(t => t.GetFields(DeclaredFields)).Where(IsSerialized);
        }

        /// <summary>The field a property path points to (e.g. "drivetrain.gearOverview.Array.data[2].gear"), or null.</summary>
        public static FieldInfo GetField(Type rootType, string propertyPath)
        {
            Type type = rootType;
            FieldInfo field = null;
            string[] parts = propertyPath.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data["))
                {
                    type = ElementType(type);
                    i++;
                    if (type == null)
                        return null;
                    continue;
                }

                field = FindField(type, parts[i]);
                if (field == null)
                    return null;
                type = field.FieldType;
            }
            return field;
        }

        /// <summary>Element type of an array or List&lt;T&gt;, or null.</summary>
        public static Type ElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return type.GetGenericArguments()[0];
            return null;
        }

        public static bool IsSerialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized)
                return false;
            return field.IsPublic || field.IsDefined(typeof(SerializeField), true);
        }

        /// <summary>Plain [Serializable] class or struct that Unity serializes inline (where nested tunables can live).</summary>
        public static bool IsNestedSerializable(Type type) =>
            !type.IsPrimitive && !type.IsEnum && !type.IsArray && type != typeof(string) &&
            !typeof(Object).IsAssignableFrom(type) &&
            !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) &&
            type.IsDefined(typeof(SerializableAttribute), false) &&
            type.Namespace?.StartsWith("UnityEngine") != true;

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, DeclaredFields);
                if (field != null)
                    return field;
            }
            return null;
        }
    }
}
