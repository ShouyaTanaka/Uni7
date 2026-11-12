#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class EventDebuggerWindow : EditorWindow
{
    private EventDefinitionDatabase database;
    private Vector2 eventScroll;
    private Vector2 historyScroll;
    private readonly Dictionary<string, object> values = new();
    private readonly Dictionary<string, bool> foldouts = new();
    private readonly List<EventHistoryEntry> history = new();
    private string searchQuery = "";
    private bool showHistory = true;

    private const int MaxHistoryCount = 100;

    private const string DatabasePath =
        "Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset";

    [MenuItem("Tools/Uni7/Event Debugger")]
    public static void Open()
    {
        GetWindow<EventDebuggerWindow>("Event Debugger");
    }

    private void OnEnable()
    {
        LoadDatabase();
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange _)
    {
        Repaint();
    }

    private void Update()
    {
        if (EditorApplication.isPlaying)
            Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(4);

        if (database == null)
        {
            EditorGUILayout.HelpBox(
                "EventDefinitionDatabaseが見つかりません。先にEvent Manager Generatorで生成してください。",
                MessageType.Warning);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                $"{database.categories.Count} categories / {CountVisibleEvents()} events",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                EditorApplication.isPlaying ? "Events can be emitted" : "Enter Play Mode to emit",
                EditorStyles.miniLabel);
        }

        eventScroll = EditorGUILayout.BeginScrollView(eventScroll);

        if (CountVisibleEvents() == 0)
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrWhiteSpace(searchQuery)
                    ? "イベントが登録されていません。Event Manager Generatorでイベントを追加してください。"
                    : "検索条件に一致するイベントがありません。",
                MessageType.Info);
        }
        else
        {
            foreach (EventCategoryDefinition category in database.categories)
            {
                if (HasVisibleEvent(category))
                    DrawCategory(category);
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);
        DrawHistory();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Event Debugger", EditorStyles.boldLabel, GUILayout.Width(110));

            if (GUILayout.Button(
                    new GUIContent("Refresh", "Reload the event database"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(65)))
            {
                database = null;
                LoadDatabase();
                Repaint();
            }

            GUILayout.FlexibleSpace();

            searchQuery = EditorGUILayout.TextField(
                searchQuery,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(150),
                GUILayout.MaxWidth(260));

            if (GUILayout.Button(
                    new GUIContent("×", "Clear search"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(22)))
            {
                searchQuery = "";
                GUI.FocusControl(null);
            }
        }
    }

    private void DrawHistory()
    {
        EditorGUILayout.BeginVertical("box");

        using (new EditorGUILayout.HorizontalScope())
        {
            showHistory = EditorGUILayout.Foldout(
                showHistory,
                $"Event History ({history.Count})",
                true,
                EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(history.Count == 0))
                if (GUILayout.Button("Clear", GUILayout.Width(70)))
                    history.Clear();
        }

        if (showHistory && history.Count == 0)
        {
            EditorGUILayout.LabelField(
                "まだイベントは発火されていません。",
                EditorStyles.miniLabel);
        }
        else if (showHistory)
        {
            historyScroll = EditorGUILayout.BeginScrollView(
                historyScroll,
                GUILayout.MinHeight(45),
                GUILayout.MaxHeight(180));

            for (int i = history.Count - 1; i >= 0; i--)
            {
                EventHistoryEntry entry = history[i];

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(entry.time, EditorStyles.miniLabel, GUILayout.Width(80));
                    EditorGUILayout.LabelField(entry.eventName, EditorStyles.boldLabel, GUILayout.Width(220));
                    EditorGUILayout.LabelField(
                        string.IsNullOrEmpty(entry.value) ? "-" : entry.value,
                        EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawCategory(EventCategoryDefinition category)
    {
        if (!foldouts.ContainsKey(category.categoryName))
            foldouts[category.categoryName] = true;

        EditorGUILayout.BeginVertical("box");

        foldouts[category.categoryName] = EditorGUILayout.Foldout(
            foldouts[category.categoryName],
            $"{category.categoryName} ({CountVisibleEvents(category)}/{category.events.Count})",
            true,
            EditorStyles.boldLabel);

        if (foldouts[category.categoryName])
        {
            foreach (EventDefinition e in category.events)
            {
                if (MatchesFilter(category, e))
                    DrawEvent(category, e);
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    private void DrawEvent(EventCategoryDefinition category, EventDefinition e)
    {
        string key = $"{category.categoryName}.{e.eventName}";

        EditorGUILayout.BeginVertical("helpbox");

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(e.eventName, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Emit", GUILayout.Width(70), GUILayout.Height(24)))
                    Emit(category, e);
            }
        }

        if (!string.IsNullOrWhiteSpace(e.comment))
            EditorGUILayout.LabelField(e.comment, EditorStyles.wordWrappedMiniLabel);

        if (e.parameterType != EventParameterType.None)
            DrawValue(key, e);
        else
            EditorGUILayout.LabelField("No parameters", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    private int CountVisibleEvents()
    {
        int count = 0;

        foreach (EventCategoryDefinition category in database.categories)
            count += CountVisibleEvents(category);

        return count;
    }

    private int CountVisibleEvents(EventCategoryDefinition category)
    {
        int count = 0;

        foreach (EventDefinition e in category.events)
        {
            if (MatchesFilter(category, e))
                count++;
        }

        return count;
    }

    private bool HasVisibleEvent(EventCategoryDefinition category)
    {
        return CountVisibleEvents(category) > 0;
    }

    private bool MatchesFilter(EventCategoryDefinition category, EventDefinition e)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return true;

        string query = searchQuery.Trim();
        return category.categoryName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
               e.eventName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
               (!string.IsNullOrEmpty(e.comment) &&
                e.comment.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void DrawValue(string key, EventDefinition e)
    {
        object old = values.TryGetValue(key, out object v)
            ? v
            : DefaultValue(e.parameterType);

        switch (e.parameterType)
        {
            case EventParameterType.Int:
                values[key] = EditorGUILayout.IntField("Value", old is int i ? i : 0);
                break;
            case EventParameterType.Float:
                values[key] = EditorGUILayout.FloatField("Value", old is float f ? f : 0f);
                break;
            case EventParameterType.String:
                values[key] = EditorGUILayout.TextField("Value", old as string ?? "");
                break;
            case EventParameterType.Bool:
                values[key] = EditorGUILayout.Toggle("Value", old is bool b && b);
                break;
            case EventParameterType.Vector2:
                values[key] = EditorGUILayout.Vector2Field("Value", old is Vector2 v2 ? v2 : Vector2.zero);
                break;
            case EventParameterType.Vector3:
                values[key] = EditorGUILayout.Vector3Field("Value", old is Vector3 v3 ? v3 : Vector3.zero);
                break;
            case EventParameterType.GameObject:
                values[key] = EditorGUILayout.ObjectField("Value", old as GameObject, typeof(GameObject), true);
                break;
        }
    }

    private void Emit(EventCategoryDefinition category, EventDefinition e)
    {
        try
        {
            Type managerType = FindType("EventManager");
            if (managerType == null)
            {
                Debug.LogError("[Event Debugger] EventManagerが見つかりません。");
                return;
            }

            PropertyInfo categoryProperty = managerType.GetProperty(
                category.categoryName,
                BindingFlags.Public | BindingFlags.Static);

            object categoryObject = categoryProperty?.GetValue(null);

            if (categoryObject == null)
            {
                Debug.LogError($"[Event Debugger] Categoryが見つかりません: {category.categoryName}");
                return;
            }

            MethodInfo emit = categoryObject.GetType().GetMethod(
                $"Emit{e.eventName}",
                BindingFlags.Public | BindingFlags.Instance);

            if (emit == null)
            {
                Debug.LogError($"[Event Debugger] Emitメソッドが見つかりません: Emit{e.eventName}");
                return;
            }

            if (e.parameterType == EventParameterType.None)
            {
                emit.Invoke(categoryObject, null);
            }
            else
            {
                string key = $"{category.categoryName}.{e.eventName}";
                object value = values.TryGetValue(key, out object v)
                    ? v
                    : DefaultValue(e.parameterType);

                emit.Invoke(categoryObject, new[] { value });
            }

            string valueText = "";

            if (e.parameterType != EventParameterType.None)
            {
                string historyKey = $"{category.categoryName}.{e.eventName}";

                if (values.TryGetValue(historyKey, out object historyValue))
                    valueText = FormatValue(historyValue);
            }

            AddHistory(
                $"{category.categoryName}.{e.eventName}",
                valueText);

            Debug.Log(
                $"[Event Debugger] Emit: {category.categoryName}.{e.eventName}" +
                (string.IsNullOrEmpty(valueText) ? "" : $" ({valueText})"));
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void AddHistory(string eventName, string value)
    {
        history.Add(new EventHistoryEntry
        {
            time = DateTime.Now.ToString("HH:mm:ss.fff"),
            eventName = eventName,
            value = value
        });

        while (history.Count > MaxHistoryCount)
            history.RemoveAt(0);

        Repaint();
    }

    private static string FormatValue(object value)
    {
        if (value == null)
            return "null";

        if (value is GameObject gameObject)
            return gameObject != null ? gameObject.name : "null";

        return value.ToString();
    }

    private static object DefaultValue(EventParameterType type)
    {
        return type switch
        {
            EventParameterType.Int => 0,
            EventParameterType.Float => 0f,
            EventParameterType.String => "",
            EventParameterType.Bool => false,
            EventParameterType.Vector2 => Vector2.zero,
            EventParameterType.Vector3 => Vector3.zero,
            EventParameterType.GameObject => null,
            _ => null
        };
    }

    private static Type FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(name);
            if (type != null)
                return type;
        }

        return null;
    }

    private void LoadDatabase()
    {
        if (database == null)
        {
            database = AssetDatabase.LoadAssetAtPath<EventDefinitionDatabase>(
                DatabasePath);
        }
    }

    private sealed class EventHistoryEntry
    {
        public string time;
        public string eventName;
        public string value;
    }
}
#endif
