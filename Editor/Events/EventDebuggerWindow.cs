#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Uni7.Events
{
    public sealed class EventDebuggerWindow : EditorWindow
    {
        private const string DatabasePath = "Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset";
        private const int MaxHistoryCount = 100;
        private EventDefinitionDatabase database;
        private Vector2 eventScroll;
        private Vector2 historyScroll;
        private readonly Dictionary<string, object> values = new();
        private readonly Dictionary<string, bool> foldouts = new();
        private readonly List<EventHistoryEntry> history = new();
        private string searchQuery = "";
        private bool showHistory = true;

        [MenuItem("Tools/Uni7/Events/Debugger")]
        public static void Open() => GetWindow<EventDebuggerWindow>("Event Debugger");

        private void OnEnable()
        {
            LoadDatabase();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        private void OnPlayModeStateChanged(PlayModeStateChange _) => Repaint();
        private void Update() { if (EditorApplication.isPlaying) Repaint(); }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            if (database == null)
            {
                EditorGUILayout.HelpBox("EventDefinitionDatabaseが見つかりません。先にEvent Manager Generatorで生成してください。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{database.categories.Count} categories / {CountVisibleEvents()} events", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(EditorApplication.isPlaying ? "Events can be emitted" : "Enter Play Mode to emit", EditorStyles.miniLabel);
            }

            eventScroll = EditorGUILayout.BeginScrollView(eventScroll);
            if (CountVisibleEvents() == 0)
                EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(searchQuery) ? "イベントが登録されていません。" : "検索条件に一致するイベントがありません。", MessageType.Info);
            else
                foreach (EventCategoryDefinition category in database.categories.Where(x => x != null && x.events != null && HasVisibleEvent(x))) DrawCategory(category);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(10);
            DrawHistory();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Event Debugger", EditorStyles.boldLabel, GUILayout.Width(110));
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) { database = null; LoadDatabase(); Repaint(); }
                GUILayout.FlexibleSpace();
                searchQuery = EditorGUILayout.TextField(searchQuery, EditorStyles.toolbarSearchField, GUILayout.MinWidth(150), GUILayout.MaxWidth(260));
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) { searchQuery = ""; GUI.FocusControl(null); }
            }
        }

        private void DrawHistory()
        {
            EditorGUILayout.BeginVertical("box");
            using (new EditorGUILayout.HorizontalScope())
            {
                showHistory = EditorGUILayout.Foldout(showHistory, $"Event History ({history.Count})", true, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(history.Count == 0)) if (GUILayout.Button("Clear", GUILayout.Width(70))) history.Clear();
            }
            if (showHistory && history.Count == 0) EditorGUILayout.LabelField("まだイベントは発火されていません。", EditorStyles.miniLabel);
            else if (showHistory)
            {
                historyScroll = EditorGUILayout.BeginScrollView(historyScroll, GUILayout.MinHeight(45), GUILayout.MaxHeight(180));
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    EventHistoryEntry item = history[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(item.time, EditorStyles.miniLabel, GUILayout.Width(80));
                        EditorGUILayout.LabelField(item.eventName, EditorStyles.boldLabel, GUILayout.Width(220));
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(item.value) ? "-" : item.value, EditorStyles.miniLabel);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCategory(EventCategoryDefinition category)
        {
            if (!foldouts.ContainsKey(category.categoryName)) foldouts[category.categoryName] = true;
            EditorGUILayout.BeginVertical("box");
            foldouts[category.categoryName] = EditorGUILayout.Foldout(foldouts[category.categoryName], $"{category.categoryName} ({CountVisibleEvents(category)}/{category.events.Count})", true, EditorStyles.boldLabel);
            if (foldouts[category.categoryName]) foreach (EventDefinition definition in category.events.Where(x => x != null && MatchesFilter(category, x))) DrawEvent(category, definition);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawEvent(EventCategoryDefinition category, EventDefinition definition)
        {
            EnsureParameters(definition);
            string key = $"{category.categoryName}.{definition.eventName}";
            EditorGUILayout.BeginVertical("helpbox");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(definition.eventName, EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying)) if (GUILayout.Button("Emit", GUILayout.Width(70), GUILayout.Height(24))) Emit(category, definition);
            }
            if (!string.IsNullOrWhiteSpace(definition.comment)) EditorGUILayout.LabelField(definition.comment, EditorStyles.wordWrappedMiniLabel);
            if (definition.parameters.Count == 0) EditorGUILayout.LabelField("No parameters", EditorStyles.miniLabel);
            for (int i = 0; i < definition.parameters.Count; i++)
            {
                EventParameterDefinition parameter = definition.parameters[i];
                string valueKey = key + "." + i;
                object old = values.TryGetValue(valueKey, out object existing) ? existing : DefaultValue(parameter.parameterType);
                values[valueKey] = DrawValue($"{parameter.parameterName} ({parameter.parameterType})", parameter.parameterType, old);
            }
            EditorGUILayout.EndVertical();
        }

        private object DrawValue(string label, EventParameterType type, object old)
        {
            switch (type)
            {
                case EventParameterType.Int: return EditorGUILayout.IntField(label, old is int i ? i : 0);
                case EventParameterType.Float: return EditorGUILayout.FloatField(label, old is float f ? f : 0f);
                case EventParameterType.String: return EditorGUILayout.TextField(label, old as string ?? "");
                case EventParameterType.Bool: return EditorGUILayout.Toggle(label, old is bool b && b);
                case EventParameterType.Vector2: return EditorGUILayout.Vector2Field(label, old is Vector2 v2 ? v2 : Vector2.zero);
                case EventParameterType.Vector3: return EditorGUILayout.Vector3Field(label, old is Vector3 v3 ? v3 : Vector3.zero);
                case EventParameterType.GameObject: return EditorGUILayout.ObjectField(label, old as GameObject, typeof(GameObject), true);
                default: return null;
            }
        }

        private void Emit(EventCategoryDefinition category, EventDefinition definition)
        {
            try
            {
                EnsureParameters(definition);
                Type managerType = FindUniqueType("EventManager");
                PropertyInfo property = managerType.GetProperty(category.categoryName, BindingFlags.Public | BindingFlags.Static);
                object categoryObject = property?.GetValue(null);
                if (categoryObject == null) throw new InvalidOperationException($"Categoryが見つかりません: {category.categoryName}");
                Type[] expectedTypes = definition.parameters.Select(x => GetParameterType(x.parameterType)).ToArray();
                MethodInfo[] candidates = categoryObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => x.Name == "Emit" + definition.eventName)
                    .Where(x => x.GetParameters().Select(p => p.ParameterType).SequenceEqual(expectedTypes)).ToArray();
                if (candidates.Length != 1) throw new InvalidOperationException(candidates.Length == 0
                    ? $"Emit{definition.eventName} が定義と一致しません。Event Generatorで再生成してください。"
                    : $"Emit{definition.eventName} が複数見つかりました。");

                object[] args = new object[definition.parameters.Count];
                List<string> display = new();
                for (int i = 0; i < args.Length; i++)
                {
                    string valueKey = $"{category.categoryName}.{definition.eventName}.{i}";
                    args[i] = values.TryGetValue(valueKey, out object value) ? value : DefaultValue(definition.parameters[i].parameterType);
                    display.Add(definition.parameters[i].parameterName + "=" + FormatValue(args[i]));
                }
                candidates[0].Invoke(categoryObject, args);
                string valueText = string.Join(", ", display);
                AddHistory($"{category.categoryName}.{definition.eventName}", valueText);
                Debug.Log($"[Event Debugger] Emit: {category.categoryName}.{definition.eventName}" + (valueText.Length == 0 ? "" : $" ({valueText})"));
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void AddHistory(string eventName, string value)
        {
            history.Add(new EventHistoryEntry { time = DateTime.Now.ToString("HH:mm:ss.fff"), eventName = eventName, value = value });
            while (history.Count > MaxHistoryCount) history.RemoveAt(0);
            Repaint();
        }

        private static object DefaultValue(EventParameterType type) => type switch
        {
            EventParameterType.Int => 0, EventParameterType.Float => 0f, EventParameterType.String => "",
            EventParameterType.Bool => false, EventParameterType.Vector2 => Vector2.zero,
            EventParameterType.Vector3 => Vector3.zero, EventParameterType.GameObject => null, _ => null
        };

        private static Type GetParameterType(EventParameterType type) => type switch
        {
            EventParameterType.Int => typeof(int), EventParameterType.Float => typeof(float), EventParameterType.String => typeof(string),
            EventParameterType.Bool => typeof(bool), EventParameterType.Vector2 => typeof(Vector2), EventParameterType.Vector3 => typeof(Vector3),
            EventParameterType.GameObject => typeof(GameObject), _ => null
        };

        private static string FormatValue(object value) => value is GameObject go ? (go != null ? go.name : "null") : value?.ToString() ?? "null";

        private static Type FindUniqueType(string fullName)
        {
            Type[] found = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).Where(t => t != null).ToArray();
            if (found.Length == 0) throw new InvalidOperationException($"{fullName} が見つかりません。Event Generatorで生成してください。");
            if (found.Length > 1) throw new InvalidOperationException($"{fullName} が複数Assemblyに存在します。");
            return found[0];
        }

        private static void EnsureParameters(EventDefinition definition)
        {
            if (definition.parameters == null) definition.parameters = new List<EventParameterDefinition>();
            if (definition.parameters.Count == 0 && definition.parameterType != EventParameterType.None)
                definition.parameters.Add(new EventParameterDefinition { parameterName = "value", parameterType = definition.parameterType });
            definition.parameterType = definition.parameters.Count == 1 && definition.parameters[0] != null ? definition.parameters[0].parameterType : EventParameterType.None;
        }

        private int CountVisibleEvents() => database.categories.Where(x => x != null).Sum(CountVisibleEvents);
        private int CountVisibleEvents(EventCategoryDefinition category) => category.events.Count(x => x != null && MatchesFilter(category, x));
        private bool HasVisibleEvent(EventCategoryDefinition category) => CountVisibleEvents(category) > 0;
        private bool MatchesFilter(EventCategoryDefinition category, EventDefinition definition) => string.IsNullOrWhiteSpace(searchQuery) ||
            (category.categoryName ?? "").IndexOf(searchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
            (definition.eventName ?? "").IndexOf(searchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
            (definition.comment ?? "").IndexOf(searchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private void LoadDatabase() { if (database == null) database = AssetDatabase.LoadAssetAtPath<EventDefinitionDatabase>(DatabasePath); }
        private sealed class EventHistoryEntry { public string time; public string eventName; public string value; }
    }
}
#endif
