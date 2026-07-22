using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Loom;
using Loom.Components;
using Loom.Entities;
using Loom.Systems;
using UnityEditor;
using UnityEngine;

namespace Loom.Unity.Editor
{
    /// <summary>
    /// Play-mode entity inspector: lists alive entities, editable component values, ECS hierarchy,
    /// archetypes/chunks, system timings, and singletons for the selected <see cref="LoomRunner"/>.
    /// </summary>
    public sealed class LoomEntityDebuggerWindow : EditorWindow
    {
        private const string PrefAutoRefresh = "Loom.EntityDebugger.AutoRefresh";
        private const string PrefHierarchy = "Loom.EntityDebugger.Hierarchy";
        private const string PrefTab = "Loom.EntityDebugger.Tab";
        private const string PrefSystemProfiling = "Loom.EntityDebugger.SystemProfiling";
        private const string PrefSortSystemsByTime = "Loom.EntityDebugger.SortSystemsByTime";

        private LoomRunner? _runner;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _filter = "";
        private int _selectedId = -1;
        private int _selectedVersion;
        private int _selectedArchetypeId = -1;
        private bool _autoRefresh = true;
        private bool _showHierarchy;
        private bool _systemProfiling = true;
        private bool _sortSystemsByTime = true;
        private int _tab;
        private double _nextRepaint;
        private readonly List<Entity> _entities = new List<Entity>(256);
        private readonly List<ArchetypeDebugInfo> _archetypes = new List<ArchetypeDebugInfo>(32);
        private readonly List<SystemProfileInfo> _systemProfiles = new List<SystemProfileInfo>(32);
        private readonly Dictionary<int, EntityBehaviour> _behaviours = new Dictionary<int, EntityBehaviour>();
        private readonly HashSet<int> _expandedComponents = new HashSet<int>();

        [MenuItem("Window/Loom/Entity Debugger")]
        public static void Open()
        {
            var window = GetWindow<LoomEntityDebuggerWindow>();
            window.titleContent = new GUIContent("Loom Entities");
            window.minSize = new Vector2(520, 320);
            window.Show();
        }

        public static void Open(LoomRunner runner, Entity entity)
        {
            var window = GetWindow<LoomEntityDebuggerWindow>();
            window.titleContent = new GUIContent("Loom Entities");
            window._runner = runner;
            window._tab = 0;
            window._selectedId = entity.Id;
            window._selectedVersion = entity.Version;
            LoomDebuggerSceneOverlay.SetSelection(runner, entity);
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            _autoRefresh = EditorPrefs.GetBool(PrefAutoRefresh, true);
            _showHierarchy = EditorPrefs.GetBool(PrefHierarchy, false);
            _systemProfiling = EditorPrefs.GetBool(PrefSystemProfiling, true);
            _sortSystemsByTime = EditorPrefs.GetBool(PrefSortSystemsByTime, true);
            _tab = EditorPrefs.GetInt(PrefTab, 0);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _selectedId = -1;
                _selectedArchetypeId = -1;
                _entities.Clear();
                _archetypes.Clear();
                _systemProfiles.Clear();
                _behaviours.Clear();
            }

            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (!_autoRefresh || !EditorApplication.isPlaying)
                return;
            if (EditorApplication.timeSinceStartup < _nextRepaint)
                return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.2;
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect a live Loom world.", MessageType.Info);
                return;
            }

            if (_runner == null)
                _runner = FindObjectOfType<LoomRunner>();

            if (_runner == null || _runner.Runtime == null || _runner.Systems == null)
            {
                EditorGUILayout.HelpBox("No LoomRunner with a Runtime found in the scene.", MessageType.Warning);
                return;
            }

            RefreshCaches(_runner.Runtime, _runner.Systems);
            ApplySystemProfiling(_runner.Systems);

            var nextTab = GUILayout.Toolbar(_tab, new[] { "Entities", "Archetypes", "Systems" });
            if (nextTab != _tab)
            {
                _tab = nextTab;
                EditorPrefs.SetInt(PrefTab, _tab);
            }

            if (_tab == 2)
            {
                DrawSystemsTab(_runner.Systems);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (_tab == 0)
            {
                DrawEntityList();
                DrawEntityDetails(_runner.World);
            }
            else
            {
                DrawArchetypeList();
                DrawArchetypeDetails();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _runner = (LoomRunner?)EditorGUILayout.ObjectField(
                    _runner, typeof(LoomRunner), true, GUILayout.Width(220));

                _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    Repaint();

                var auto = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(44));
                if (auto != _autoRefresh)
                {
                    _autoRefresh = auto;
                    EditorPrefs.SetBool(PrefAutoRefresh, _autoRefresh);
                }

                if (_tab == 0)
                {
                    var hier = GUILayout.Toggle(_showHierarchy, "Hierarchy", EditorStyles.toolbarButton, GUILayout.Width(70));
                    if (hier != _showHierarchy)
                    {
                        _showHierarchy = hier;
                        EditorPrefs.SetBool(PrefHierarchy, _showHierarchy);
                    }
                }

                GUILayout.FlexibleSpace();

                if (_runner != null && _runner.Runtime != null)
                {
                    GUILayout.Label($"Entities: {_runner.World.EntityCount}", EditorStyles.miniLabel);
                    if (_tab == 2 && _systemProfiles.Count > 0)
                    {
                        double total = 0;
                        for (int i = 0; i < _systemProfiles.Count; i++)
                        {
                            if (!_systemProfiles[i].IsGroup)
                                total += _systemProfiles[i].LastMilliseconds;
                        }

                        GUILayout.Label($"Systems: {total:F2} ms", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void ApplySystemProfiling(SystemGroup systems)
        {
            if (systems.ProfilingEnabled != _systemProfiling)
                systems.ProfilingEnabled = _systemProfiling;
        }
        private void RefreshCaches(Runtime runtime, SystemGroup systems)
        {
            var world = runtime.World;
            _entities.Clear();
            world.ForEachAlive(e => _entities.Add(e));

            _archetypes.Clear();
            world.ForEachArchetype(a => _archetypes.Add(a));

            _systemProfiles.Clear();
            systems.CollectProfileInfos("Systems", _systemProfiles, includeNested: true);
            if (_sortSystemsByTime)
            {
                _systemProfiles.Sort((a, b) =>
                    b.LastMilliseconds.CompareTo(a.LastMilliseconds));
            }

            _behaviours.Clear();
            var behaviours = FindObjectsOfType<EntityBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b != null && b.IsBound && b.Runner == _runner)
                    _behaviours[b.Entity.Id] = b;
            }
        }

        private void DrawEntityList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(180, position.width * 0.34f))))
            {
                EditorGUILayout.LabelField("Entities", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

                if (_showHierarchy)
                    DrawHierarchyList();
                else
                    DrawFlatList();

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawFlatList()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                if (!PassesFilter(entity))
                    continue;
                DrawEntityRow(entity, 0);
            }
        }

        private void DrawHierarchyList()
        {
            var world = _runner!.World;
            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                if (world.HasFather(entity))
                    continue;
                DrawHierarchyNode(world, entity, 0);
            }
        }

        private void DrawHierarchyNode(World world, Entity entity, int depth)
        {
            if (PassesFilter(entity) || HasMatchingDescendant(world, entity))
                DrawEntityRow(entity, depth);

            foreach (var child in world.GetChildren(entity))
                DrawHierarchyNode(world, child, depth + 1);
        }

        private bool HasMatchingDescendant(World world, Entity entity)
        {
            if (string.IsNullOrEmpty(_filter))
                return true;

            foreach (var child in world.GetChildren(entity))
            {
                if (PassesFilter(child) || HasMatchingDescendant(world, child))
                    return true;
            }

            return false;
        }

        private void DrawEntityRow(Entity entity, int depth)
        {
            bool selected = entity.Id == _selectedId && entity.Version == _selectedVersion;
            var label = BuildEntityLabel(entity);
            var style = selected ? EditorStyles.boldLabel : EditorStyles.label;

            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            rect.xMin += depth * 14f;

            if (GUI.Toggle(rect, selected, label, style) && !selected)
            {
                _selectedId = entity.Id;
                _selectedVersion = entity.Version;
                LoomDebuggerSceneOverlay.SetSelection(_runner, entity);
                GUI.FocusControl(null);
            }
        }

        private string BuildEntityLabel(Entity entity)
        {
            var sb = new StringBuilder(32);
            sb.Append(entity.Id).Append(':').Append(entity.Version);
            if (_behaviours.TryGetValue(entity.Id, out var behaviour) && behaviour != null)
                sb.Append("  ").Append(behaviour.gameObject.name);
            return sb.ToString();
        }

        private bool PassesFilter(Entity entity)
        {
            if (string.IsNullOrEmpty(_filter))
                return true;

            if (entity.Id.ToString().IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (_behaviours.TryGetValue(entity.Id, out var behaviour) && behaviour != null &&
                behaviour.gameObject.name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private void DrawEntityDetails(World world)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                if (_selectedId < 0 || !world.TryGetAliveEntity(_selectedId, out var entity) ||
                    entity.Version != _selectedVersion)
                {
                    EditorGUILayout.HelpBox("Select an entity.", MessageType.None);
                    DrawSingletons(world);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                EditorGUILayout.LabelField("Entity", $"{entity.Id}:{entity.Version}");

                if (world.TryGetEntityArchetype(entity, out var arch))
                {
                    EditorGUILayout.LabelField("Archetype",
                        $"#{arch.Id}  ({arch.EntityCount} entities, {arch.ChunkCount} chunks)");
                    if (GUILayout.Button("Show archetype", GUILayout.Width(120)))
                    {
                        _tab = 1;
                        _selectedArchetypeId = arch.Id;
                        EditorPrefs.SetInt(PrefTab, _tab);
                    }
                }

                var father = world.GetFather(entity);
                EditorGUILayout.LabelField("Father", father.IsNull ? "(root)" : $"{father.Id}:{father.Version}");

                if (world.HasChildren(entity))
                {
                    var children = new StringBuilder();
                    foreach (var child in world.GetChildren(entity))
                    {
                        if (children.Length > 0)
                            children.Append(", ");
                        children.Append(child.Id).Append(':').Append(child.Version);
                    }

                    EditorGUILayout.LabelField("Children", children.ToString());
                }

                if (_behaviours.TryGetValue(entity.Id, out var behaviour) && behaviour != null)
                {
                    EditorGUILayout.ObjectField("GameObject", behaviour.gameObject, typeof(GameObject), true);
                    if (GUILayout.Button("Ping GameObject", GUILayout.Width(140)))
                        EditorGUIUtility.PingObject(behaviour.gameObject);
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Components", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Edits write back into the live World (shared components mutate the interned value).", MessageType.None);

                int index = 0;
                world.ForEachComponent(entity, info =>
                {
                    DrawComponent(world, entity, info, index++);
                });

                if (index == 0)
                    EditorGUILayout.LabelField("(no components)", EditorStyles.miniLabel);

                EditorGUILayout.Space(10);
                DrawSingletons(world);

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawComponent(World world, Entity entity, ComponentDebugInfo info, int index)
        {
            var header = info.IsTag
                ? $"{info.Type.Name}  [Tag]"
                : $"{info.Type.Name}  [{info.Kind}]";

            bool expanded = _expandedComponents.Contains(index);
            bool next = EditorGUILayout.Foldout(expanded, header, true);
            if (next != expanded)
            {
                if (next)
                    _expandedComponents.Add(index);
                else
                    _expandedComponents.Remove(index);
            }

            if (!next)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (info.IsTag)
                {
                    EditorGUILayout.LabelField("tag (no fields)");
                    return;
                }

                if (info.Value == null)
                {
                    EditorGUILayout.LabelField("(null)");
                    return;
                }

                object boxed = info.Value;
                if (DrawBoxedValueEditable(ref boxed) && world.TrySetComponent(entity, info.Type, boxed))
                    GUI.changed = true;
            }
        }

        private void DrawArchetypeList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(180, position.width * 0.34f))))
            {
                EditorGUILayout.LabelField("Archetypes", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

                for (int i = 0; i < _archetypes.Count; i++)
                {
                    var arch = _archetypes[i];
                    if (!string.IsNullOrEmpty(_filter) &&
                        arch.Id.ToString().IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        !ArchetypeMatchesFilter(arch))
                        continue;

                    bool selected = arch.Id == _selectedArchetypeId;
                    var label = $"#{arch.Id}  e={arch.EntityCount}  c={arch.ChunkCount}";
                    var style = selected ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Toggle(selected, label, style) && !selected)
                        _selectedArchetypeId = arch.Id;
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private bool ArchetypeMatchesFilter(ArchetypeDebugInfo arch)
        {
            for (int i = 0; i < arch.ComponentTypes.Length; i++)
            {
                if (arch.ComponentTypes[i].Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void DrawArchetypeDetails()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Archetype", EditorStyles.boldLabel);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                ArchetypeDebugInfo? selected = null;
                for (int i = 0; i < _archetypes.Count; i++)
                {
                    if (_archetypes[i].Id == _selectedArchetypeId)
                    {
                        selected = _archetypes[i];
                        break;
                    }
                }

                if (selected == null)
                {
                    EditorGUILayout.HelpBox("Select an archetype.", MessageType.None);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                var arch = selected.Value;
                EditorGUILayout.LabelField("Id", arch.Id.ToString());
                EditorGUILayout.LabelField("Entities", arch.EntityCount.ToString());
                EditorGUILayout.LabelField("Chunks", $"{arch.ChunkCount} × {1024} rows/chunk");
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Dense component types", EditorStyles.boldLabel);
                for (int i = 0; i < arch.ComponentTypes.Length; i++)
                    EditorGUILayout.LabelField($"• {arch.ComponentTypes[i].Name}");

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSystemsTab(SystemGroup systems)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    var profiling = GUILayout.Toggle(
                        _systemProfiling, "Profiling", EditorStyles.toolbarButton, GUILayout.Width(70));
                    if (profiling != _systemProfiling)
                    {
                        _systemProfiling = profiling;
                        EditorPrefs.SetBool(PrefSystemProfiling, _systemProfiling);
                        systems.ProfilingEnabled = _systemProfiling;
                    }

                    var sort = GUILayout.Toggle(
                        _sortSystemsByTime, "Sort by time", EditorStyles.toolbarButton, GUILayout.Width(90));
                    if (sort != _sortSystemsByTime)
                    {
                        _sortSystemsByTime = sort;
                        EditorPrefs.SetBool(PrefSortSystemsByTime, _sortSystemsByTime);
                    }

                    if (GUILayout.Button("Reset max/avg", EditorStyles.toolbarButton, GUILayout.Width(100)))
                        systems.ResetProfiling();

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        _systemProfiling
                            ? "Per-system Update() wall time (ms). Playback not included."
                            : "Enable Profiling to record timings.",
                        EditorStyles.miniLabel);
                }

                if (_systemProfiles.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No systems registered on this LoomRunner.Systems group.",
                        MessageType.Info);
                    return;
                }

                double totalLast = 0;
                double totalAvg = 0;
                int enabledCount = 0;
                for (int i = 0; i < _systemProfiles.Count; i++)
                {
                    var row = _systemProfiles[i];
                    if (row.IsGroup)
                        continue;
                    totalLast += row.LastMilliseconds;
                    totalAvg += row.AverageMilliseconds;
                    if (row.Enabled)
                        enabledCount++;
                }

                EditorGUILayout.LabelField(
                    $"Systems: {_systemProfiles.Count}  ·  enabled leaves: {enabledCount}  ·  last Σ {totalLast:F3} ms  ·  avg Σ {totalAvg:F3} ms",
                    EditorStyles.boldLabel);

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Stage", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                    GUILayout.Label("System", EditorStyles.miniBoldLabel, GUILayout.MinWidth(160));
                    GUILayout.Label("Last", EditorStyles.miniBoldLabel, GUILayout.Width(64));
                    GUILayout.Label("Avg", EditorStyles.miniBoldLabel, GUILayout.Width(64));
                    GUILayout.Label("Max", EditorStyles.miniBoldLabel, GUILayout.Width(64));
                    GUILayout.Label("n", EditorStyles.miniBoldLabel, GUILayout.Width(40));
                    GUILayout.Label("Flags", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                }

                string? filter = string.IsNullOrWhiteSpace(_filter) ? null : _filter.Trim();
                for (int i = 0; i < _systemProfiles.Count; i++)
                {
                    var row = _systemProfiles[i];
                    if (filter != null &&
                        row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        row.Stage.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        row.Type.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    DrawSystemProfileRow(row, totalLast);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawSystemProfileRow(SystemProfileInfo row, double totalLast)
        {
            float share = totalLast > 0.0001 && !row.IsGroup
                ? (float)(row.LastMilliseconds / totalLast)
                : 0f;

            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            if (share > 0f)
            {
                var bar = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(share), rect.height);
                EditorGUI.DrawRect(bar, new Color(0.2f, 0.45f, 0.75f, 0.22f));
            }

            float x = rect.x;
            float h = rect.height;
            EditorGUI.LabelField(new Rect(x, rect.y, 120, h), row.Stage);
            x += 120;
            float nameW = Mathf.Max(160, rect.width - 120 - 64 * 3 - 40 - 100);
            string label = row.IsGroup ? $"▾ {row.Name}" : row.Name;
            EditorGUI.LabelField(new Rect(x, rect.y, nameW, h), label);
            x += nameW;
            EditorGUI.LabelField(new Rect(x, rect.y, 64, h), $"{row.LastMilliseconds:F3}");
            x += 64;
            EditorGUI.LabelField(new Rect(x, rect.y, 64, h), $"{row.AverageMilliseconds:F3}");
            x += 64;
            EditorGUI.LabelField(new Rect(x, rect.y, 64, h), $"{row.MaxMilliseconds:F3}");
            x += 64;
            EditorGUI.LabelField(new Rect(x, rect.y, 40, h), row.SampleCount.ToString());
            x += 40;

            var flags = new StringBuilder(16);
            if (!row.Enabled) flags.Append("off ");
            if (row.IsParallel) flags.Append("parallel ");
            if (row.IsGroup) flags.Append("group ");
            EditorGUI.LabelField(new Rect(x, rect.y, 100, h), flags.ToString().Trim());
        }

        private static bool DrawBoxedValueEditable(ref object value)
        {
            var type = value.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length == 0)
            {
                EditorGUILayout.LabelField(value.ToString());
                return false;
            }

            bool changed = false;
            object boxed = value;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                object? fieldValue = field.GetValue(boxed);
                if (DrawFieldEditable(field.Name, field.FieldType, ref fieldValue))
                {
                    field.SetValue(boxed, fieldValue);
                    changed = true;
                }
            }

            if (changed)
                value = boxed;
            return changed;
        }

        private static bool DrawFieldEditable(string name, Type type, ref object? value)
        {
            EditorGUI.BeginChangeCheck();
            if (type == typeof(int))
                value = EditorGUILayout.IntField(name, value is int i ? i : 0);
            else if (type == typeof(float))
                value = EditorGUILayout.FloatField(name, value is float f ? f : 0f);
            else if (type == typeof(bool))
                value = EditorGUILayout.Toggle(name, value is bool b && b);
            else if (type == typeof(string))
                value = EditorGUILayout.TextField(name, value as string ?? "");
            else if (type == typeof(double))
                value = EditorGUILayout.DoubleField(name, value is double d ? d : 0d);
            else if (type == typeof(long))
                value = EditorGUILayout.LongField(name, value is long l ? l : 0L);
            else if (type.IsEnum)
                value = EditorGUILayout.EnumPopup(name, value as Enum ?? (Enum)Activator.CreateInstance(type)!);
            else if (value != null && type.IsValueType)
            {
                EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
                object nested = value;
                bool nestedChanged;
                using (new EditorGUI.IndentLevelScope())
                    nestedChanged = DrawBoxedValueEditable(ref nested);
                if (nestedChanged)
                    value = nested;
                return nestedChanged;
            }
            else
            {
                EditorGUILayout.LabelField(name, value?.ToString() ?? "null");
                return false;
            }

            return EditorGUI.EndChangeCheck();
        }

        private void DrawSingletons(World world)
        {
            EditorGUILayout.LabelField("Singletons", EditorStyles.boldLabel);
            int count = 0;
            world.ForEachSingletonDebug((type, value) =>
            {
                count++;
                EditorGUILayout.LabelField(type.Name, EditorStyles.miniBoldLabel);
                object boxed = value;
                using (new EditorGUI.IndentLevelScope())
                {
                    if (DrawBoxedValueEditable(ref boxed))
                        world.SetSingletonDebug(type, boxed);
                }
            });

            if (count == 0)
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
        }
    }
}
