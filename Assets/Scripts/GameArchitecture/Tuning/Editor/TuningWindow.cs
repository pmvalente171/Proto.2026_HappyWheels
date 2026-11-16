using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameArchitecture.Tuning.Editing
{
    /// <summary>
    /// Window > Tuning. Shows every field marked [Tunable] on ScriptableObject assets (including fields inside
    /// [Serializable] classes they contain), grouped into tabs and folders from the attribute. Edits apply live
    /// during play, and Save writes them to disk.
    /// </summary>
    public class TuningWindow : EditorWindow
    {
        private const int MaxNestingDepth = 8;

        /// <summary>One folder in a tab's tree.</summary>
        private class Node
        {
            public readonly string Name;
            public readonly string Key; // unique, for remembering whether it's folded
            public readonly List<Node> Children = new();
            public readonly List<string> PropertyPaths = new();

            public Node(string name, string key)
            {
                Name = name;
                Key = key;
            }

            public Node GetOrAddChild(string name)
            {
                Node child = Children.Find(c => c.Name == name);
                if (child != null)
                    return child;

                child = new Node(name, Key + "/" + name);
                Children.Add(child);
                return child;
            }
        }

        /// <summary>One asset's fields within one tab.</summary>
        private class Group
        {
            public Object Asset;
            public SerializedObject SerializedObject;
            public Node Root;
        }

        [SerializeField] private int selectedTab;
        [SerializeField] private string search = "";
        private Vector2 _scroll;

        private readonly List<string> _tabs = new();
        private readonly Dictionary<string, List<Group>> _groupsByTab = new();
        private readonly List<Object> _assets = new();
        private readonly Dictionary<Object, string> _savedSnapshots = new();

        [MenuItem("Window/Tuning")]
        public static void Open() => GetWindow<TuningWindow>("Tuning");

        private void OnEnable() => Rebuild();
        private void OnProjectChange() => Rebuild();

        // Keeps the dirty markers and values current while playing
        private void OnInspectorUpdate() => Repaint();

        #region Discovery

        private void Rebuild()
        {
            _tabs.Clear();
            _groupsByTab.Clear();
            _assets.Clear();

            foreach (Type type in FindTunableAssetTypes())
            {
                List<(string propertyPath, TunableAttribute attribute)> fields = CollectTunables(type, "", 0).ToList();
                foreach (Object asset in FindAssets(type))
                    AddAsset(asset, fields);
            }

            _tabs.Sort(StringComparer.OrdinalIgnoreCase);
            selectedTab = Mathf.Clamp(selectedTab, 0, Mathf.Max(_tabs.Count - 1, 0));

            // Forget snapshots of assets that no longer exist; keep the rest so Revert still goes back to the last save
            foreach (Object gone in _savedSnapshots.Keys.Where(a => !_assets.Contains(a)).ToList())
                _savedSnapshots.Remove(gone);
            foreach (Object asset in _assets.Where(a => !_savedSnapshots.ContainsKey(a)))
                _savedSnapshots[asset] = EditorJsonUtility.ToJson(asset);

            Repaint();
        }

        private void AddAsset(Object asset, List<(string propertyPath, TunableAttribute attribute)> fields)
        {
            var serializedObject = new SerializedObject(asset);
            var groupsForThisAsset = new Dictionary<string, Group>();

            foreach ((string propertyPath, TunableAttribute attribute) in fields)
            {
                if (serializedObject.FindProperty(propertyPath) == null)
                    continue;

                if (!groupsForThisAsset.TryGetValue(attribute.Tab, out Group group))
                {
                    group = new Group
                    {
                        Asset = asset,
                        SerializedObject = serializedObject,
                        Root = new Node(attribute.Tab, $"{nameof(TuningWindow)}/{asset.GetInstanceID()}/{attribute.Tab}")
                    };
                    groupsForThisAsset[attribute.Tab] = group;

                    if (!_groupsByTab.TryGetValue(attribute.Tab, out List<Group> groups))
                    {
                        _groupsByTab[attribute.Tab] = groups = new List<Group>();
                        _tabs.Add(attribute.Tab);
                    }
                    groups.Add(group);
                }

                Node node = group.Root;
                foreach (string folder in attribute.Path.Split('/'))
                {
                    if (!string.IsNullOrWhiteSpace(folder))
                        node = node.GetOrAddChild(folder.Trim());
                }
                node.PropertyPaths.Add(propertyPath);
            }

            if (groupsForThisAsset.Count > 0)
                _assets.Add(asset);
        }

        /// <summary>ScriptableObject types that contain at least one [Tunable] field, directly or nested.</summary>
        private static IEnumerable<Type> FindTunableAssetTypes()
        {
            // Only look at assemblies that declare tunable fields, or reference one that does
            var tunableAssemblies = new HashSet<Assembly>(
                TypeCache.GetFieldsWithAttribute<TunableAttribute>().Select(f => f.DeclaringType.Assembly));
            if (tunableAssemblies.Count == 0)
                yield break;
            var tunableAssemblyNames = new HashSet<string>(tunableAssemblies.Select(a => a.GetName().Name));

            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || type.IsGenericType)
                    continue;

                Assembly assembly = type.Assembly;
                bool related = tunableAssemblies.Contains(assembly) ||
                               assembly.GetReferencedAssemblies().Any(r => tunableAssemblyNames.Contains(r.Name));
                if (related && CollectTunables(type, "", 0).Any())
                    yield return type;
            }
        }

        /// <summary>Walks a type's serialized fields, yielding the property path of every [Tunable] one.</summary>
        private static IEnumerable<(string propertyPath, TunableAttribute attribute)> CollectTunables(Type type, string prefix, int depth)
        {
            if (depth > MaxNestingDepth)
                yield break;

            // Base classes first, so inherited fields come first (as in the Inspector)
            var hierarchy = new List<Type>();
            for (Type t = type; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
                hierarchy.Insert(0, t);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in hierarchy.SelectMany(t => t.GetFields(flags)))
            {
                if (!IsSerialized(field))
                    continue;

                string path = prefix + field.Name;
                var attribute = field.GetCustomAttribute<TunableAttribute>();
                if (attribute != null)
                {
                    yield return (path, attribute);
                    continue;
                }

                if (IsNestedSerializable(field.FieldType))
                {
                    foreach (var nested in CollectTunables(field.FieldType, path + ".", depth + 1))
                        yield return nested;
                }
            }
        }

        private static bool IsSerialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized)
                return false;
            return field.IsPublic || field.IsDefined(typeof(SerializeField), true);
        }

        /// <summary>Plain [Serializable] class or struct that Unity serializes inline (where nested tunables can live).</summary>
        private static bool IsNestedSerializable(Type type) =>
            !type.IsPrimitive && !type.IsEnum && !type.IsArray && type != typeof(string) &&
            !typeof(Object).IsAssignableFrom(type) &&
            !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) &&
            type.IsDefined(typeof(SerializableAttribute), false) &&
            type.Namespace?.StartsWith("UnityEngine") != true;

        private static IEnumerable<Object> FindAssets(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset && asset.GetType() == type)
                        yield return asset;
                }
            }
        }

        #endregion

        #region Drawing

        private void OnGUI()
        {
            DrawToolbar();

            if (_tabs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nothing to tune yet. Mark fields of a ScriptableObject with [Tunable(\"Tab\", \"Folder/Subfolder\")] " +
                    "and create an asset of it.", MessageType.Info);
                return;
            }

            selectedTab = Mathf.Clamp(selectedTab, 0, _tabs.Count - 1);
            string[] labels = _tabs.Select(t => IsTabDirty(t) ? t + " *" : t).ToArray();
            selectedTab = GUILayout.Toolbar(selectedTab, labels);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            List<Group> groups = _groupsByTab[_tabs[selectedTab]];
            foreach (Group group in groups)
            {
                if (!group.Asset)
                {
                    // Deleted while the window was open; OnProjectChange will rebuild
                    continue;
                }
                DrawGroup(group);
            }
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button(new GUIContent("Refresh", "Search the project again for tunable assets."),
                        EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    Rebuild();
            }
        }

        private void DrawGroup(Group group)
        {
            // Which asset these values belong to (click it to find the asset)
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(group.Asset, group.Asset.GetType(), false);

            group.SerializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawNodeContents(group.Root, group.SerializedObject, false);
            if (EditorGUI.EndChangeCheck())
                group.SerializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
        }

        /// <param name="showAll">True when a parent folder matched the search, so everything inside it is shown.</param>
        private void DrawNodeContents(Node node, SerializedObject serializedObject, bool showAll)
        {
            foreach (string path in node.PropertyPaths)
            {
                SerializedProperty property = serializedObject.FindProperty(path);
                if (property != null && (showAll || Matches(property.displayName)))
                    EditorGUILayout.PropertyField(property, true);
            }

            foreach (Node child in node.Children)
            {
                bool childShowAll = showAll || (!string.IsNullOrEmpty(search) && Matches(child.Name));
                if (!childShowAll && !HasMatch(child, serializedObject))
                    continue;

                bool searching = !string.IsNullOrEmpty(search);
                bool open = searching || SessionState.GetBool(child.Key, true);
                bool newOpen = EditorGUILayout.Foldout(open, child.Name, true, EditorStyles.foldoutHeader);
                if (!searching && newOpen != open)
                    SessionState.SetBool(child.Key, newOpen);

                if (!newOpen)
                    continue;

                EditorGUI.indentLevel++;
                DrawNodeContents(child, serializedObject, childShowAll);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawFooter()
        {
            bool anyDirty = _assets.Any(a => a && EditorUtility.IsDirty(a));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(EditorApplication.isPlaying
                        ? "Playing: changes apply live and are kept after play."
                        : "Changes apply immediately.",
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!anyDirty))
                {
                    if (GUILayout.Button(new GUIContent("Revert", "Undo every change made since the last save (or since the window opened)."),
                            GUILayout.Width(70f)))
                        RevertAll();

                    if (GUILayout.Button(new GUIContent("Save", "Write all changed tuning assets to disk."), GUILayout.Width(70f)))
                        SaveAll();
                }
            }
        }

        private bool Matches(string text) =>
            string.IsNullOrEmpty(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private bool HasMatch(Node node, SerializedObject serializedObject)
        {
            if (string.IsNullOrEmpty(search) || Matches(node.Name))
                return true;
            if (node.PropertyPaths.Any(p => serializedObject.FindProperty(p) is { } prop && Matches(prop.displayName)))
                return true;
            return node.Children.Any(c => HasMatch(c, serializedObject));
        }

        private bool IsTabDirty(string tab) =>
            _groupsByTab[tab].Any(g => g.Asset && EditorUtility.IsDirty(g.Asset));

        #endregion

        #region Save / revert

        private void SaveAll()
        {
            foreach (Object asset in _assets.Where(a => a && EditorUtility.IsDirty(a)))
            {
                AssetDatabase.SaveAssetIfDirty(asset);
                _savedSnapshots[asset] = EditorJsonUtility.ToJson(asset);
            }
        }

        private void RevertAll()
        {
            foreach (Object asset in _assets.Where(a => a && EditorUtility.IsDirty(a)))
            {
                if (!_savedSnapshots.TryGetValue(asset, out string json))
                    continue;

                Undo.RecordObject(asset, "Revert Tuning");
                EditorJsonUtility.FromJsonOverwrite(json, asset);
                EditorUtility.ClearDirty(asset);
            }

            foreach (Group group in _groupsByTab.Values.SelectMany(g => g))
                group.SerializedObject.Update();
        }

        #endregion
    }
}
