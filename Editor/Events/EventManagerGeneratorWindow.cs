#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Uni7.Events
{
    public class EventManagerGeneratorWindow : EditorWindow
    {
        private const string DatabasePath = "Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset";
        private const string GeneratedFolder = "Assets/Scripts/GenerateEvents";
        private const string GeneratedCategoriesFolder = GeneratedFolder + "/Categories";
        private const string EventManagerPath = GeneratedFolder + "/EventManager.cs";

        private EventDefinitionDatabase database;

        private string newCategoryName = "";
        private string newEventName = "";
        private EventParameterType newParameterType = EventParameterType.None;

        private Vector2 scroll;
        private string searchQuery = "";
        private bool showInstructions = false;
        private bool showCreatePanel = true;
        private Vector2 navigationScroll;
        private Vector2 inspectorScroll;
        private string selectedEventKey = "";

        // Foldout state is kept per category/event during this editor session.
        private readonly Dictionary<string, bool> categoryFoldouts = new();
        private readonly Dictionary<string, bool> eventFoldouts = new();

        [MenuItem("Tools/Uni7/Events/Generator")]
        public static void Open()
        {
            GetWindow<EventManagerGeneratorWindow>("Event Manager");
        }

        private void OnEnable()
        {
            minSize = new Vector2(680f, 520f);
            titleContent = new GUIContent("Event Generator");
            LoadOrCreateDatabase();
        }

        private void OnGUI()
        {
            if (database == null)
            {
                LoadOrCreateDatabase();
                if (database == null) return;
            }

            EnsureDatabaseCollections();
            DrawToolbar();
            DrawStatusHeader();

            if (showInstructions)
                EditorGUILayout.HelpBox("左側でカテゴリ・イベントを選択し、右側で詳細を編集します。ウィンドウ幅に応じて自動的にレイアウトが切り替わります。", MessageType.Info);

            DrawCreatePanel();
            DrawWorkspace();
            DrawActionBar();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Uni7 / Event Manager", EditorStyles.boldLabel, GUILayout.Width(160));
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(55)))
                {
                    database = null;
                    LoadOrCreateDatabase();
                    selectedEventKey = "";
                    Repaint();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label("Search", EditorStyles.miniLabel, GUILayout.Width(42));
                searchQuery = EditorGUILayout.TextField(searchQuery, EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(70), GUILayout.MaxWidth(240));
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(24)))
                {
                    searchQuery = "";
                    GUI.FocusControl(null);
                }
                showInstructions = GUILayout.Toggle(showInstructions, "Help", EditorStyles.toolbarButton, GUILayout.Width(48));
            }
        }

        private void DrawStatusHeader()
        {
            int categoryCount = database.categories.Count(x => x != null);
            int eventCount = database.categories.Where(x => x != null && x.events != null).Sum(x => x.events.Count(e => e != null));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("EVENT DEFINITIONS", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"{categoryCount} Categories", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField($"{eventCount} Events", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                }
                EditorGUILayout.LabelField("生成先", GeneratedFolder, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("現在の選択", string.IsNullOrEmpty(selectedEventKey) ? "イベント未選択" : selectedEventKey, EditorStyles.miniLabel);
            }
        }

        private void DrawCreatePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    showCreatePanel = EditorGUILayout.Foldout(showCreatePanel, "追加・作成", true, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (!showCreatePanel) GUILayout.Label("カテゴリ / イベント", EditorStyles.miniLabel);
                }
                if (!showCreatePanel) return;

                float width = Mathf.Max(260f, position.width - 45f);
                bool narrow = width < 760f;
                Action drawCategoryCreator = () =>
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("カテゴリを作成", EditorStyles.miniBoldLabel);
                        newCategoryName = EditorGUILayout.TextField(newCategoryName, GUILayout.MinHeight(24), GUILayout.ExpandWidth(true));
                        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newCategoryName)))
                            if (GUILayout.Button("＋ カテゴリ", GUILayout.Height(25))) AddCategory();
                    }
                };
                Action drawEventCreator = () =>
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                    {
                        EditorGUILayout.LabelField("イベントを作成", EditorStyles.miniBoldLabel);
                        var validCategories = database.categories.Where(x => x != null).ToList();
                        if (validCategories.Count == 0) EditorGUILayout.HelpBox("先にカテゴリを作成してください。", MessageType.Info);
                        else
                        {
                            string[] names = validCategories.Select(x => x.categoryName).ToArray();
                            int index = Mathf.Clamp(EditorPrefs.GetInt("EventManagerGenerator.CategoryIndex", 0), 0, names.Length - 1);
                            index = EditorGUILayout.Popup("追加先", index, names);
                            EditorPrefs.SetInt("EventManagerGenerator.CategoryIndex", index);
                            newEventName = EditorGUILayout.TextField("イベント名", newEventName, GUILayout.MinHeight(24));
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                newParameterType = (EventParameterType)EditorGUILayout.EnumPopup("初期引数", newParameterType, GUILayout.MinWidth(120));
                                GUILayout.FlexibleSpace();
                                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newEventName)))
                                    if (GUILayout.Button("＋ イベント", GUILayout.Width(105), GUILayout.Height(24))) AddEvent(index);
                            }
                        }
                    }
                };
                if (narrow)
                {
                    using (new EditorGUILayout.VerticalScope()) { drawCategoryCreator(); drawEventCreator(); }
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope()) { drawCategoryCreator(); drawEventCreator(); }
                }
            }
        }

        private void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("出力先: " + GeneratedFolder, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(55))) SaveDatabase();
                if (GUILayout.Button("保存して生成", EditorStyles.toolbarButton, GUILayout.Width(105)))
                {
                    SaveDatabase();
                    GenerateAll();
                }
            }
        }

        private void DrawWorkspace()
        {
            float availableWidth = position.width;
            bool wide = availableWidth >= 1050f;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (wide)
                {
                    DrawNavigationPane(GUILayout.Width(Mathf.Clamp(availableWidth * 0.30f, 280f, 370f)), GUILayout.ExpandHeight(true));
                    DrawInspectorPane(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                }
                else
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawNavigationPane(GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Clamp(position.height * 0.34f, 170f, 290f)));
                        DrawInspectorPane(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    }
                }
            }
        }

        private void DrawNavigationPane(params GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, options))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("カテゴリ・イベント", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{database.categories.Count} カテゴリ", EditorStyles.miniLabel);
                }
                int visibleCategories = 0;
                var eventsToDelete = new List<(EventCategoryDefinition category, EventDefinition definition)>();
                using (var navigationScope = new EditorGUILayout.ScrollViewScope(navigationScroll, GUILayout.ExpandHeight(true)))
                {
                    navigationScroll = navigationScope.scrollPosition;
                for (int i = 0; i < database.categories.Count; i++)
                {
                    EventCategoryDefinition category = database.categories[i];
                    if (category == null || category.events == null || !HasVisibleEvent(category)) continue;
                    visibleCategories++;
                    string categoryKey = category.categoryName ?? $"Category_{i}";
                    if (!categoryFoldouts.ContainsKey(categoryKey)) categoryFoldouts[categoryKey] = true;
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            categoryFoldouts[categoryKey] = EditorGUILayout.Foldout(categoryFoldouts[categoryKey], category.categoryName, true, EditorStyles.boldLabel);
                            GUILayout.Label(GetVisibleEventCount(category).ToString(), EditorStyles.miniLabel, GUILayout.Width(22));
                            if (GUILayout.Button("⋮", EditorStyles.miniButton, GUILayout.Width(22)))
                            {
                                var menu = new GenericMenu();
                                string capturedName = category.categoryName;
                                menu.AddItem(new GUIContent("カテゴリを削除"), false, () =>
                                {
                                    if (EditorUtility.DisplayDialog("カテゴリ削除", $"「{capturedName}」と所属イベントを削除しますか？", "削除", "キャンセル"))
                                    {
                                        database.categories.RemoveAll(x => x != null && x.categoryName == capturedName);
                                        if (selectedEventKey.StartsWith(capturedName + ".", StringComparison.Ordinal)) selectedEventKey = "";
                                        EditorUtility.SetDirty(database);
                                    }
                                });
                                menu.ShowAsContext();
                            }
                        }
                        if (categoryFoldouts[categoryKey])
                        {
                            foreach (EventDefinition e in category.events)
                            {
                                if (e == null || !MatchesFilter(category, e)) continue;
                                string key = category.categoryName + "." + e.eventName;
                                bool selected = selectedEventKey == key;
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    GUIStyle rowStyle = new GUIStyle(selected ? EditorStyles.toolbarButton : EditorStyles.label)
                                    { alignment = TextAnchor.MiddleLeft, stretchWidth = true, fixedHeight = 27f };
                                    if (GUILayout.Button(new GUIContent("   " + (e.eventName ?? "(名称未設定)"), $"{e.parameters?.Count ?? 0} parameters"), rowStyle))
                                    {
                                        selectedEventKey = key;
                                        GUI.FocusControl(null);
                                    }
                                    GUILayout.Label((e.parameters?.Count ?? 0).ToString(), EditorStyles.miniLabel, GUILayout.Width(20));
                                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
                                    {
                                        if (EditorUtility.DisplayDialog("イベント削除", $"「{e.eventName}」を削除しますか？", "削除", "キャンセル"))
                                            eventsToDelete.Add((category, e));
                                    }
                                }
                            }
                        }
                    }
                }
                }
                foreach (var item in eventsToDelete)
                {
                    item.category.events.Remove(item.definition);
                    if (selectedEventKey == item.category.categoryName + "." + item.definition.eventName) selectedEventKey = "";
                    EditorUtility.SetDirty(database);
                }
                if (database.categories.Count == 0)
                    EditorGUILayout.HelpBox("カテゴリがありません。「追加・作成」から作成してください。", MessageType.Info);
                else if (visibleCategories == 0)
                    EditorGUILayout.HelpBox("検索条件に一致するイベントがありません。検索語を変更してください。", MessageType.Info);
            }
        }

        private void DrawInspectorPane(params GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, options))
            {
                EventCategoryDefinition selectedCategory = null;
                EventDefinition selectedEvent = null;
                foreach (var category in database.categories)
                {
                    if (category == null || category.events == null) continue;
                    var found = category.events.FirstOrDefault(e => e != null && selectedEventKey == category.categoryName + "." + e.eventName);
                    if (found != null) { selectedCategory = category; selectedEvent = found; break; }
                }

                if (selectedEvent == null)
                {
                    EditorGUILayout.LabelField("イベント詳細", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.HelpBox("編集するイベントを選択してください。\n\n左側の一覧からイベントを選ぶと、ここに詳細と引数が表示されます。", MessageType.Info);
                    GUILayout.FlexibleSpace();
                    return;
                }

                using (var inspectorScope = new EditorGUILayout.ScrollViewScope(inspectorScroll, GUILayout.ExpandHeight(true)))
                {
                inspectorScroll = inspectorScope.scrollPosition;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("イベント詳細", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(selectedCategory.categoryName, EditorStyles.miniButton);
                }
                EditorGUILayout.LabelField("選択中のイベント", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(selectedEvent.eventName, EditorStyles.largeLabel);
                EditorGUILayout.Space(6);

                EditorGUILayout.LabelField("基本情報", EditorStyles.boldLabel);
                string oldName = selectedEvent.eventName ?? "";
                string editedName = EditorGUILayout.TextField("イベント名", oldName);
                if (editedName != oldName)
                {
                    selectedEvent.eventName = editedName;
                    selectedEventKey = selectedCategory.categoryName + "." + editedName;
                    EditorUtility.SetDirty(database);
                }
                EditorGUILayout.LabelField("カテゴリ", selectedCategory.categoryName);
                EditorGUILayout.LabelField("説明 / コメント", EditorStyles.miniBoldLabel);
                selectedEvent.comment = EditorGUILayout.TextArea(selectedEvent.comment ?? "", GUILayout.MinHeight(55), GUILayout.ExpandWidth(true));

                EditorGUILayout.Space(10);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("引数", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{selectedEvent.parameters?.Count ?? 0} 個", EditorStyles.miniLabel);
                }
                EnsureParameterList(selectedEvent);
                var removals = new List<int>();
                for (int i = 0; i < selectedEvent.parameters.Count; i++)
                {
                    var parameter = selectedEvent.parameters[i];
                    if (parameter == null) { removals.Add(i); continue; }
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{i + 1:00}", EditorStyles.miniBoldLabel, GUILayout.Width(28));
                            parameter.parameterName = EditorGUILayout.TextField(parameter.parameterName ?? "", GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
                            parameter.parameterType = (EventParameterType)EditorGUILayout.EnumPopup(parameter.parameterType, GUILayout.MinWidth(110), GUILayout.Width(145));
                            if (GUILayout.Button("削除", GUILayout.Width(48))) removals.Add(i);
                        }
                    }
                }
                for (int i = removals.Count - 1; i >= 0; i--) selectedEvent.parameters.RemoveAt(removals[i]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("＋ 引数を追加", GUILayout.Width(120), GUILayout.Height(26)))
                        selectedEvent.parameters.Add(new EventParameterDefinition { parameterName = CreateUniqueParameterName(selectedEvent), parameterType = EventParameterType.String });
                }
                selectedEvent.parameterType = selectedEvent.parameters.Count == 1 ? selectedEvent.parameters[0].parameterType : EventParameterType.None;
                if (selectedEvent.parameters.Count == 0)
                    EditorGUILayout.HelpBox("引数なしイベントです。", MessageType.None);
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("引数の順序は生成されるPayload型とEmitメソッドに反映されます。変更後は「保存して生成」を実行してください。", MessageType.None);
                }
                if (GUI.changed) EditorUtility.SetDirty(database);
            }
        }

        private static string CreateUniqueParameterName(EventDefinition definition)
        {
            int suffix = 1;
            string candidate;
            do
            {
                candidate = "parameter" + suffix++;
            }
            while (definition.parameters.Any(x => x != null && string.Equals(x.parameterName, candidate, StringComparison.OrdinalIgnoreCase)));
            return candidate;
        }

        private void EnsureDatabaseCollections()
        {
            if (database.categories == null)
                database.categories = new List<EventCategoryDefinition>();
            database.categories.RemoveAll(category => category == null);
            foreach (EventCategoryDefinition category in database.categories)
            {
                if (category.events == null)
                    category.events = new List<EventDefinition>();
            }
        }

        private static void EnsureParameterList(EventDefinition definition)
        {
            if (definition.parameters == null)
                definition.parameters = new List<EventParameterDefinition>();

            // Migrate legacy assets that stored a single parameterType.
            if (definition.parameters.Count == 0 && definition.parameterType != EventParameterType.None)
            {
                definition.parameters.Add(new EventParameterDefinition
                {
                    parameterName = "value",
                    parameterType = definition.parameterType
                });
            }

            // Keep the legacy field synchronized for older serialized consumers only.
            definition.parameterType = definition.parameters.Count == 1 && definition.parameters[0] != null
                ? definition.parameters[0].parameterType
                : EventParameterType.None;
        }

        private bool HasVisibleEvent(EventCategoryDefinition category)
        {
            if (category == null || category.events == null)
                return false;
            return string.IsNullOrWhiteSpace(searchQuery) || GetVisibleEventCount(category) > 0;
        }

        private int GetVisibleEventCount(EventCategoryDefinition category)
        {
            if (category == null || category.events == null)
                return 0;
            if (string.IsNullOrWhiteSpace(searchQuery))
                return category.events.Count;

            int count = 0;

            foreach (EventDefinition e in category.events)
            {
                if (MatchesFilter(category, e))
                    count++;
            }

            return count;
        }

        private bool MatchesFilter(EventCategoryDefinition category, EventDefinition e)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;

            string query = searchQuery.Trim();
            return (category.categoryName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (e.eventName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (!string.IsNullOrEmpty(e.comment) &&
                    e.comment.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void AddCategory()
        {
            string name = SanitizeIdentifier(newCategoryName);

            if (string.IsNullOrEmpty(name))
            {
                EditorUtility.DisplayDialog("Error", "カテゴリ名を入力してください。", "OK");
                return;
            }

            if (database.categories.Any(x => x != null && string.Equals(x.categoryName, name, StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog("Error", "同名カテゴリが存在します。", "OK");
                return;
            }

            database.categories.Add(new EventCategoryDefinition
            {
                categoryName = name
            });

            newCategoryName = "";
            SaveDatabase();
            Repaint();
        }

        private void AddEvent(int categoryIndex)
        {
            string name = SanitizeIdentifier(newEventName);

            if (string.IsNullOrEmpty(name))
            {
                EditorUtility.DisplayDialog("Error", "イベント名を入力してください。", "OK");
                return;
            }

            EventCategoryDefinition category = database.categories[categoryIndex];

            if (category.events.Any(x => x != null && string.Equals(x.eventName, name, StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog("Error", "同名イベントが存在します。", "OK");
                return;
            }

            var definition = new EventDefinition
            {
                eventName = name,
                comment = "",
                parameterType = newParameterType,
                parameters = new List<EventParameterDefinition>()
            };
            if (newParameterType != EventParameterType.None)
            {
                definition.parameters.Add(new EventParameterDefinition
                {
                    parameterName = "value",
                    parameterType = newParameterType
                });
            }
            category.events.Add(definition);

            newEventName = "";
            newParameterType = EventParameterType.None;

            SaveDatabase();
            Repaint();
        }

        private void LoadOrCreateDatabase()
        {
            // EnsureFolder("Assets/EventSystem");
            EnsureFolder(GeneratedFolder);
            EnsureFolder(GeneratedCategoriesFolder);

            database = AssetDatabase.LoadAssetAtPath<EventDefinitionDatabase>(DatabasePath);

            if (database == null)
            {
                database = CreateInstance<EventDefinitionDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private void SaveDatabase()
        {
            if (database == null)
                return;

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void GenerateAll()
        {
            if (database == null)
                return;

            EnsureFolder(GeneratedFolder);
            EnsureFolder(GeneratedCategoriesFolder);
            string validationError = ValidateDefinitions();
            if (!string.IsNullOrEmpty(validationError))
            {
                EditorUtility.DisplayDialog("Event Manager - 定義エラー", validationError, "OK");
                return;
            }
            if (IsUIModuleEnabled())
                EnsureUICategoryDefinitions();

            // Preflight every output before writing anything. Never replace a user-authored file.
            List<string> outputPaths = database.categories
                .Where(category => category != null && !string.IsNullOrWhiteSpace(category.categoryName))
                .Select(category => $"{GeneratedCategoriesFolder}/{category.categoryName}Events.cs")
                .ToList();
            outputPaths.Add(EventManagerPath);
            List<string> blocked = outputPaths.Where(path => !CanOverwriteGeneratedFile(path)).ToList();
            if (blocked.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Event Manager - 安全のため中止",
                    "生成先に手動作成または生成元不明のファイルがあります。上書きせずに中止しました。\n\n" +
                    string.Join("\n", blocked),
                    "OK");
                return;
            }

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();

            // Build every output in memory first. A generation error cannot leave mixed code generations.
            Dictionary<string, string> generatedFiles = new Dictionary<string, string>();
            try
            {
                foreach (EventCategoryDefinition category in database.categories)
                {
                    if (category == null) continue;
                    generatedFiles[$"{GeneratedCategoriesFolder}/{category.categoryName}Events.cs"] = BuildCategory(category);
                }
                generatedFiles[EventManagerPath] = BuildEventManager();
                WriteGeneratedFilesAtomically(generatedFiles);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Event Manager - 生成失敗", "コード生成またはファイル反映に失敗しました。既存の生成ファイルは可能な限り復元しています。\n\n" + exception.Message, "OK");
                return;
            }

            AssetDatabase.Refresh();

            // Stale generated classes are intentionally retained instead of being deleted automatically.
            string[] existingScripts = AssetDatabase.FindAssets("t:MonoScript", new[] { GeneratedCategoriesFolder });
            HashSet<string> expected = new HashSet<string>(outputPaths, StringComparer.OrdinalIgnoreCase);
            List<string> stale = existingScripts.Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("Events.cs", StringComparison.Ordinal) && !expected.Contains(path))
                .ToList();

            string message = "イベントコードの生成が完了しました。\n既存の手動ファイルは上書きせず、不要になった生成ファイルも自動削除していません。";
            if (stale.Count > 0)
                message += "\n\n未使用と思われる生成ファイル（内容を確認してから手動で削除してください）:\n" + string.Join("\n", stale);
            EditorUtility.DisplayDialog("Event Manager", message, "OK");
        }

        private string ValidateDefinitions()
        {
            if (database.categories == null)
                return "カテゴリ一覧が初期化されていません。";
            var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EventCategoryDefinition category in database.categories)
            {
                if (category == null || !IsValidIdentifier(category.categoryName))
                    return "カテゴリ名がC#の識別子として不正です。";
                if (!categoryNames.Add(category.categoryName))
                    return $"カテゴリ名が重複しています（大文字・小文字の違いを含む）: {category.categoryName}";
                if (string.Equals(category.categoryName, "Instance", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(category.categoryName, "EventManager", StringComparison.Ordinal) ||
                    string.Equals(category.categoryName, "Dispose", StringComparison.Ordinal) ||
                    string.Equals(category.categoryName, "InitializeOnPlay", StringComparison.Ordinal) ||
                    string.Equals(category.categoryName, "ResetStaticState", StringComparison.Ordinal) ||
                    string.Equals(category.categoryName, "Disposed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(category.categoryName, "_instance", StringComparison.Ordinal) ||
                    string.Equals(category.categoryName, "_disposed", StringComparison.Ordinal))
                    return $"カテゴリ名「{category.categoryName}」は生成される EventManager のメンバー名と衝突するため使用できません。";

                if (category.events == null)
                    return $"{category.categoryName} のイベント一覧が初期化されていません。";
                var eventNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (EventDefinition definition in category.events)
                {
                    if (definition == null || !IsValidIdentifier(definition.eventName))
                        return $"{category.categoryName} に不正なイベント名があります。";
                    if (!eventNames.Add(definition.eventName))
                        return $"イベント名が重複しています: {category.categoryName}.{definition.eventName}";

                    EnsureParameterList(definition);
                    var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (EventParameterDefinition parameter in definition.parameters)
                    {
                        if (parameter == null || !IsValidIdentifier(parameter.parameterName))
                            return $"{definition.eventName} に不正な引数名があります。";
                        if (!parameterNames.Add(parameter.parameterName))
                            return $"{definition.eventName} の引数名が重複しています: {parameter.parameterName}";
                        if (!Enum.IsDefined(typeof(EventParameterType), parameter.parameterType) || parameter.parameterType == EventParameterType.None)
                            return $"{definition.eventName}.{parameter.parameterName} の型が不正です。None 以外の有効な型を指定してください。";
                    }
                }
            }

            // Built-in UI events are connected to UIEventHub and must keep their contract.
            EventCategoryDefinition uiCategory = database.categories.FirstOrDefault(x => x != null && x.categoryName == "UI");
            if (uiCategory != null)
            {
                if (!HasExpectedUIEvent(uiCategory, "OpenWindow", EventParameterType.String) ||
                    !HasExpectedUIEvent(uiCategory, "OpenModal", EventParameterType.String) ||
                    !HasExpectedUIEvent(uiCategory, "CloseUI", EventParameterType.String) ||
                    !HasExpectedUIEvent(uiCategory, "CloseTopModal", EventParameterType.None) ||
                    !HasExpectedUIEvent(uiCategory, "CloseAll", EventParameterType.None))
                    return "UIカテゴリの標準イベント（OpenWindow / OpenModal / CloseUI / CloseTopModal / CloseAll）の引数は変更できません。";
            }
            return null;
        }

        private static bool HasExpectedUIEvent(EventCategoryDefinition category, string eventName, EventParameterType expectedType)
        {
            EventDefinition definition = category.events.FirstOrDefault(x => x != null && x.eventName == eventName);
            if (definition == null) return true;
            EnsureParameterList(definition);
            if (expectedType == EventParameterType.None) return definition.parameters.Count == 0;
            return definition.parameters.Count == 1 && definition.parameters[0].parameterType == expectedType;
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || CSharpKeywords.Contains(value)) return false;
            if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
            return value.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private static bool CanOverwriteGeneratedFile(string path)
        {
            if (!File.Exists(path))
                return true;

            string content;
            try { content = File.ReadAllText(path); }
            catch { return false; }

            return content.Contains("// <auto-generated>") &&
                   content.Contains("Generated by EventManagerGeneratorWindow.");
        }


        private static bool IsUIModuleEnabled()
        {
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
                return false;

            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            return symbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(symbol => string.Equals(symbol.Trim(), "UNI7_UI", StringComparison.Ordinal));
        }

        private void EnsureUICategoryDefinitions()
        {
            EventCategoryDefinition category = database.categories.FirstOrDefault(x => x.categoryName == "UI");
            if (category == null)
            {
                category = new EventCategoryDefinition { categoryName = "UI" };
                database.categories.Add(category);
            }

            AddDefaultUIEvent(category, "OpenWindow", EventParameterType.String, "WindowをUI IDで開きます。");
            AddDefaultUIEvent(category, "OpenModal", EventParameterType.String, "ModalをUI IDで開きます。");
            AddDefaultUIEvent(category, "CloseUI", EventParameterType.String, "指定したUI IDを閉じます。");
            AddDefaultUIEvent(category, "CloseTopModal", EventParameterType.None, "最前面のModalを閉じます。");
            AddDefaultUIEvent(category, "CloseAll", EventParameterType.None, "表示中のUIをすべて閉じます。");
        }

        private static void AddDefaultUIEvent(EventCategoryDefinition category, string name, EventParameterType type, string comment)
        {
            EventDefinition existing = category.events.FirstOrDefault(x => x.eventName == name);
            if (existing != null)
            {
                // ValidateDefinitions already checks built-in UI event signatures. Preserve user parameter names and comments.
                if (string.IsNullOrWhiteSpace(existing.comment)) existing.comment = comment;
                return;
            }
            category.events.Add(new EventDefinition
            {
                eventName = name,
                parameterType = type,
                comment = comment,
                parameters = type == EventParameterType.None
                    ? new List<EventParameterDefinition>()
                    : new List<EventParameterDefinition> { new EventParameterDefinition { parameterName = "value", parameterType = type } }
            });
        }

        private string BuildCategory(EventCategoryDefinition category)
        {
            string className = category.categoryName + "Events";
            StringBuilder sb = new();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by EventManagerGeneratorWindow.");
            sb.AppendLine("// Do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Cysharp.Threading.Tasks;");
            sb.AppendLine("using UniRx;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            foreach (EventDefinition e in category.events)
            {
                EnsureParameterList(e);
                if (e.parameters.Count > 1)
                {
                    string payload = GetPayloadTypeName(category.categoryName, e);
                    sb.AppendLine($"public readonly struct {payload}");
                    sb.AppendLine("{");
                    foreach (EventParameterDefinition parameter in e.parameters)
                        sb.AppendLine($"    public readonly {GetParameterType(parameter.parameterType)} {ToPascalCase(parameter.parameterName)};");
                    sb.AppendLine();
                    sb.AppendLine($"    public {payload}({string.Join(", ", e.parameters.Select(p => GetParameterType(p.parameterType) + " " + p.parameterName))})");
                    sb.AppendLine("    {");
                    foreach (EventParameterDefinition parameter in e.parameters)
                        sb.AppendLine($"        {ToPascalCase(parameter.parameterName)} = {parameter.parameterName};");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"public sealed class {className} : IDisposable");
            sb.AppendLine("{");
            sb.AppendLine("    private bool disposed;");
            foreach (EventDefinition e in category.events)
            {
                EnsureParameterList(e);
                string type = GetEventPayloadType(category.categoryName, e);
                sb.AppendLine($"    private readonly Subject<{type}> _{e.eventName} = new();");
            }

            if (category.categoryName == "UI")
            {
                sb.AppendLine("#if UNI7_UI");
                sb.AppendLine("    private readonly Action<string> hubOpenWindowHandler;");
                sb.AppendLine("    private readonly Action<string> hubOpenModalHandler;");
                sb.AppendLine("    private readonly Action<string> hubCloseUIHandler;");
                sb.AppendLine("    private readonly Action hubCloseTopModalHandler;");
                sb.AppendLine("    private readonly Action hubCloseAllHandler;");
                sb.AppendLine("    public UIEvents()");
                sb.AppendLine("    {");
                sb.AppendLine("        hubOpenWindowHandler = value => _OpenWindow.OnNext(value);");
                sb.AppendLine("        hubOpenModalHandler = value => _OpenModal.OnNext(value);");
                sb.AppendLine("        hubCloseUIHandler = value => _CloseUI.OnNext(value);");
                sb.AppendLine("        hubCloseTopModalHandler = () => _CloseTopModal.OnNext(Unit.Default);");
                sb.AppendLine("        hubCloseAllHandler = () => _CloseAll.OnNext(Unit.Default);");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenWindow += hubOpenWindowHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenModal += hubOpenModalHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseUI += hubCloseUIHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseTopModal += hubCloseTopModalHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseAll += hubCloseAllHandler;");
                sb.AppendLine("    }");
                sb.AppendLine("#endif");
            }

            foreach (EventDefinition e in category.events)
                GenerateEventMethods(sb, e, category.categoryName);

            sb.AppendLine("    public void Dispose()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (disposed) return;");
            sb.AppendLine("        disposed = true;");
            if (category.categoryName == "UI")
            {
                sb.AppendLine("#if UNI7_UI");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenWindow -= hubOpenWindowHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenModal -= hubOpenModalHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseUI -= hubCloseUIHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseTopModal -= hubCloseTopModalHandler;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseAll -= hubCloseAllHandler;");
                sb.AppendLine("#endif");
            }
            foreach (EventDefinition e in category.events)
                sb.AppendLine($"        CompleteAndDispose(_{e.eventName});");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static void CompleteAndDispose<T>(Subject<T> subject)");
            sb.AppendLine("    {");
            sb.AppendLine("        try { subject.OnCompleted(); }");
            sb.AppendLine("        catch (Exception exception) { Debug.LogException(exception); }");
            sb.AppendLine("        finally { subject.Dispose(); }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private void GenerateEventMethods(StringBuilder sb, EventDefinition e, string categoryName)
        {
            EnsureParameterList(e);
            string payloadType = GetEventPayloadType(categoryName, e);
            bool noParameter = e.parameters.Count == 0;
            AppendSummary(sb, e.comment);
            sb.AppendLine($"    // Event: {e.eventName}");

            if (noParameter)
            {
                sb.AppendLine($"    public IDisposable On{e.eventName}(Action callback) => _{e.eventName}.Subscribe(_ => callback());");
                sb.AppendLine($"    public UniTask Wait{e.eventName}(CancellationToken cancellationToken = default) => _{e.eventName}.First().ToUniTask(cancellationToken: cancellationToken);");
                if (categoryName == "UI" && e.eventName == "CloseTopModal")
                {
                    sb.AppendLine("    public void EmitCloseTopModal()\n    {\n#if UNI7_UI\n        Uni7.UI.UIEventHub.PublishCloseTopModal();\n#else\n        _CloseTopModal.OnNext(Unit.Default);\n#endif\n    }");
                }
                else if (categoryName == "UI" && e.eventName == "CloseAll")
                {
                    sb.AppendLine("    public void EmitCloseAll()\n    {\n#if UNI7_UI\n        Uni7.UI.UIEventHub.PublishCloseAll();\n#else\n        _CloseAll.OnNext(Unit.Default);\n#endif\n    }");
                }
                else
                    sb.AppendLine($"    public void Emit{e.eventName}() => _{e.eventName}.OnNext(Unit.Default);");
            }
            else if (e.parameters.Count == 1)
            {
                string type = GetParameterType(e.parameters[0].parameterType);
                string parameterName = e.parameters[0].parameterName;
                sb.AppendLine($"    public IDisposable On{e.eventName}(Action<{type}> callback) => _{e.eventName}.Subscribe(callback);");
                sb.AppendLine($"    public UniTask<{type}> Wait{e.eventName}(CancellationToken cancellationToken = default) => _{e.eventName}.First().ToUniTask(cancellationToken: cancellationToken);");
                if (categoryName == "UI" && e.eventName == "OpenWindow")
                    sb.AppendLine($"    public void Emit{e.eventName}({type} {parameterName})\n    {{\n#if UNI7_UI\n        Uni7.UI.UIEventHub.PublishOpenWindow({parameterName});\n#else\n        _{e.eventName}.OnNext({parameterName});\n#endif\n    }}");
                else if (categoryName == "UI" && e.eventName == "OpenModal")
                    sb.AppendLine($"    public void Emit{e.eventName}({type} {parameterName})\n    {{\n#if UNI7_UI\n        Uni7.UI.UIEventHub.PublishOpenModal({parameterName});\n#else\n        _{e.eventName}.OnNext({parameterName});\n#endif\n    }}");
                else if (categoryName == "UI" && e.eventName == "CloseUI")
                    sb.AppendLine($"    public void Emit{e.eventName}({type} {parameterName})\n    {{\n#if UNI7_UI\n        Uni7.UI.UIEventHub.PublishCloseUI({parameterName});\n#else\n        _{e.eventName}.OnNext({parameterName});\n#endif\n    }}");
                else
                    sb.AppendLine($"    public void Emit{e.eventName}({type} {parameterName}) => _{e.eventName}.OnNext({parameterName});");
            }
            else
            {
                string args = string.Join(", ", e.parameters.Select(p => GetParameterType(p.parameterType) + " " + p.parameterName));
                string values = string.Join(", ", e.parameters.Select(p => p.parameterName));
                sb.AppendLine($"    public IDisposable On{e.eventName}(Action<{payloadType}> callback) => _{e.eventName}.Subscribe(callback);");
                sb.AppendLine($"    public UniTask<{payloadType}> Wait{e.eventName}(CancellationToken cancellationToken = default) => _{e.eventName}.First().ToUniTask(cancellationToken: cancellationToken);");
                sb.AppendLine($"    public void Emit{e.eventName}({args}) => _{e.eventName}.OnNext(new {payloadType}({values}));");
            }
            sb.AppendLine();
        }

        private static string GetEventPayloadType(string categoryName, EventDefinition definition)
        {
            EnsureParameterList(definition);
            if (definition.parameters.Count == 0) return "Unit";
            if (definition.parameters.Count == 1) return GetParameterType(definition.parameters[0].parameterType);
            return GetPayloadTypeName(categoryName, definition);
        }

        private static string GetPayloadTypeName(string categoryName, EventDefinition definition)
        {
            // Length prefixes make the pair unambiguous: (AB, C) cannot collide with (A, BC).
            return $"Uni7Payload_C{categoryName.Length}_{categoryName}_E{definition.eventName.Length}_{definition.eventName}";
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Value";
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private string BuildEventManager()
        {
            StringBuilder sb = new();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by EventManagerGeneratorWindow.");
            sb.AppendLine("// Do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("public sealed class EventManager : IDisposable");
            sb.AppendLine("{");
            sb.AppendLine("    private static EventManager _instance;");
            sb.AppendLine("    private bool _disposed;");
            sb.AppendLine();
            sb.AppendLine("    public static EventManager Instance => _instance ??= new EventManager();");
            sb.AppendLine();
            sb.AppendLine("    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("    private static void ResetStaticState()");
            sb.AppendLine("    {");
            sb.AppendLine("        EventManager previous = _instance;");
            sb.AppendLine("        previous?.Dispose();");
            sb.AppendLine("        _instance = null;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            sb.AppendLine("    private static void InitializeOnPlay() => _ = Instance;");
            sb.AppendLine();

            foreach (EventCategoryDefinition category in database.categories)
            {
                string className = category.categoryName + "Events";
                string fieldName = ToCamelCase(category.categoryName);

                sb.AppendLine($"    private readonly {className} _{fieldName};");
            }

            sb.AppendLine();

            foreach (EventCategoryDefinition category in database.categories)
            {
                string className = category.categoryName + "Events";
                string propertyName = category.categoryName;
                string fieldName = ToCamelCase(category.categoryName);

                sb.AppendLine($"    public static {className} {propertyName} => Instance._{fieldName};");
            }

            sb.AppendLine();

            sb.AppendLine("    private EventManager()");
            sb.AppendLine("    {");

            foreach (EventCategoryDefinition category in database.categories)
            {
                string className = category.categoryName + "Events";
                string fieldName = ToCamelCase(category.categoryName);

                sb.AppendLine($"        _{fieldName} = new {className}();");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void Dispose()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_disposed) return;");
            sb.AppendLine("        _disposed = true;");

            foreach (EventCategoryDefinition category in database.categories)
            {
                string fieldName = ToCamelCase(category.categoryName);

                sb.AppendLine($"        _{fieldName}.Dispose();");
            }

            sb.AppendLine("        if (ReferenceEquals(_instance, this)) _instance = null;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void WriteGeneratedFilesAtomically(Dictionary<string, string> generatedFiles)
        {
            Dictionary<string, byte[]> backups = new Dictionary<string, byte[]>();
            foreach (string path in generatedFiles.Keys)
                backups[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;

            List<string> temporaryFiles = new List<string>();
            try
            {
                foreach (KeyValuePair<string, string> file in generatedFiles)
                {
                    string temporaryPath = Path.Combine(Path.GetTempPath(), "Uni7_" + Guid.NewGuid().ToString("N") + ".tmp");
                    temporaryFiles.Add(temporaryPath);
                    File.WriteAllText(temporaryPath, file.Value, new UTF8Encoding(false));
                    File.Copy(temporaryPath, file.Key, true);
                }
            }
            catch
            {
                foreach (KeyValuePair<string, byte[]> backup in backups)
                {
                    try
                    {
                        if (backup.Value == null)
                        {
                            if (File.Exists(backup.Key)) File.Delete(backup.Key);
                        }
                        else File.WriteAllBytes(backup.Key, backup.Value);
                    }
                    catch (Exception restoreException) { Debug.LogException(restoreException); }
                }
                throw;
            }
            finally
            {
                foreach (string temporaryPath in temporaryFiles)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (Exception cleanupException) { Debug.LogException(cleanupException); }
                }
            }
        }

        private static void AppendSummary(StringBuilder sb, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return;

            string normalized = comment
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            string[] lines = normalized.Split('\n');

            sb.AppendLine("    /// <summary>");

            foreach (string line in lines)
            {
                string escaped = line
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");

                sb.AppendLine($"    /// {escaped}");
            }

            sb.AppendLine("    /// </summary>");
        }

        private static string GetParameterType(EventParameterType type)
        {
            return type switch
            {
                EventParameterType.None => "Unit",
                EventParameterType.Int => "int",
                EventParameterType.Float => "float",
                EventParameterType.String => "string",
                EventParameterType.Bool => "bool",
                EventParameterType.Vector2 => "Vector2",
                EventParameterType.Vector3 => "Vector3",
                EventParameterType.GameObject => "GameObject",
                _ => "Unit"
            };
        }

        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "async", "await"
        };

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            StringBuilder sb = new();

            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }

            string result = sb.ToString();

            if (string.IsNullOrEmpty(result))
                return "";

            if (char.IsDigit(result[0]))
                result = "_" + result;

            if (CSharpKeywords.Contains(result))
                result = "_" + result;

            return result;
        }

        private static string ToCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folder = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif