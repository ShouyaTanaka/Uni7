#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private bool showInstructions = true;

        // Foldout state is kept per category name during this editor session.
        private readonly Dictionary<string, bool> categoryFoldouts = new();

        [MenuItem("Tools/Uni7/Events/Generator")]
        public static void Open()
        {
            GetWindow<EventManagerGeneratorWindow>("Event Manager");
        }

        private void OnEnable()
        {
            minSize = new Vector2(640f, 520f);
            titleContent = new GUIContent("Event Generator");
            LoadOrCreateDatabase();
        }

        private void OnGUI()
        {
            if (database == null)
            {
                LoadOrCreateDatabase();
                if (database == null)
                    return;
            }

            DrawToolbar();
            EditorGUILayout.Space(4);

            if (showInstructions)
            {
                EditorGUILayout.HelpBox(
                    "カテゴリとイベントを登録し、Save + Generateでコードを更新します。",
                    MessageType.Info);
            }

            DrawAddSection();
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"{database.categories.Count} categories / {database.categories.Sum(x => x.events.Count)} events",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(searchQuery) ? "Showing all events" : "Filtered",
                    EditorStyles.miniLabel);
            }

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                DrawEventList();
                scroll = scrollScope.scrollPosition;
            }

            EditorGUILayout.Space(6);
            DrawActionBar();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Event Manager Generator", EditorStyles.boldLabel, GUILayout.Width(170));

                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(55)))
                {
                    database = null;
                    LoadOrCreateDatabase();
                    Repaint();
                }

                GUILayout.FlexibleSpace();

                searchQuery = EditorGUILayout.TextField(
                    searchQuery,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(160),
                    GUILayout.MaxWidth(280));

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(42)))
                {
                    searchQuery = "";
                    GUI.FocusControl(null);
                }

                showInstructions = GUILayout.Toggle(
                    showInstructions,
                    "Help",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(48));
            }
        }

        private void DrawAddSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Add Event Definition", EditorStyles.boldLabel);
                DrawAddCategory();
                EditorGUILayout.Space(4);
                DrawAddEvent();
            }
        }

        private void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save", GUILayout.Height(30)))
                {
                    SaveDatabase();
                }

                if (GUILayout.Button("Generate", GUILayout.Height(30)))
                {
                    SaveDatabase();
                    GenerateAll();
                }

                if (GUILayout.Button("Save + Generate", GUILayout.Height(30)))
                {
                    SaveDatabase();
                    GenerateAll();
                }
            }

            EditorGUILayout.HelpBox(
                "生成先: Assets/Scripts/GenerateEvents/\n" +
                "生成ファイルは手動編集せず、このEditorから再生成してください。",
                MessageType.None);
        }

        private void DrawAddCategory()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                newCategoryName = EditorGUILayout.TextField(
                    new GUIContent("Category", "C# identifier used for the generated class"),
                    newCategoryName);

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newCategoryName)))
                    if (GUILayout.Button("Add Category", GUILayout.Width(100)))
                    {
                        AddCategory();
                    }
            }
        }

        private void DrawAddEvent()
        {
            if (database.categories.Count == 0)
            {
                EditorGUILayout.HelpBox("先にカテゴリを作成してください。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                int categoryIndex = Mathf.Max(0, EditorPrefs.GetInt("EventManagerGenerator.CategoryIndex", 0));
                categoryIndex = Mathf.Clamp(categoryIndex, 0, database.categories.Count - 1);

                string[] categoryNames = database.categories
                    .Select(x => x.categoryName)
                    .ToArray();

                categoryIndex = EditorGUILayout.Popup(categoryIndex, categoryNames);
                EditorPrefs.SetInt("EventManagerGenerator.CategoryIndex", categoryIndex);

                newEventName = EditorGUILayout.TextField(
                    new GUIContent("Event", "C# identifier used for generated methods"),
                    newEventName);
                newParameterType = (EventParameterType)EditorGUILayout.EnumPopup(
                    new GUIContent("Parameter"),
                    newParameterType);

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newEventName)))
                    if (GUILayout.Button("Add Event", GUILayout.Width(100)))
                    {
                        AddEvent(categoryIndex);
                    }
            }
        }

        private void DrawEventList()
        {
            string categoryToDelete = null;
            var eventsToDelete = new List<(EventCategoryDefinition category, EventDefinition definition)>();

            for (int categoryIndex = 0; categoryIndex < database.categories.Count; categoryIndex++)
            {
                EventCategoryDefinition category = database.categories[categoryIndex];
                if (category == null || !HasVisibleEvent(category))
                    continue;

                if (!categoryFoldouts.ContainsKey(category.categoryName))
                    categoryFoldouts[category.categoryName] = true;

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        categoryFoldouts[category.categoryName] = EditorGUILayout.Foldout(
                            categoryFoldouts[category.categoryName],
                            $"{category.categoryName}  ({GetVisibleEventCount(category)}/{category.events.Count})",
                            true,
                            EditorStyles.boldLabel);

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Delete Category", GUILayout.Width(120)) &&
                            EditorUtility.DisplayDialog("Delete Category",
                                $"「{category.categoryName}」を削除しますか？", "Delete", "Cancel"))
                        {
                            categoryToDelete = category.categoryName;
                        }
                    }

                    if (!categoryFoldouts[category.categoryName])
                        continue;

                    foreach (EventDefinition e in category.events)
                    {
                        if (e == null || !MatchesFilter(category, e))
                            continue;

                        EnsureParameterList(e);
                        using (new EditorGUILayout.VerticalScope("helpbox"))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                e.eventName = EditorGUILayout.TextField("Event", e.eventName);
                                GUILayout.FlexibleSpace();
                                if (GUILayout.Button("Delete", GUILayout.Width(70)))
                                    eventsToDelete.Add((category, e));
                            }

                            e.comment = EditorGUILayout.TextArea(e.comment ?? "", GUILayout.MinHeight(38));
                            EditorGUILayout.LabelField("Comment", EditorStyles.miniLabel);
                            EditorGUILayout.Space(3);
                            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

                            for (int parameterIndex = 0; parameterIndex < e.parameters.Count; parameterIndex++)
                            {
                                EventParameterDefinition parameter = e.parameters[parameterIndex];
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    parameter.parameterName = EditorGUILayout.TextField(parameter.parameterName);
                                    parameter.parameterType = (EventParameterType)EditorGUILayout.EnumPopup(
                                        parameter.parameterType, GUILayout.Width(120));
                                    if (GUILayout.Button("−", GUILayout.Width(25)))
                                    {
                                        e.parameters.RemoveAt(parameterIndex);
                                        parameterIndex--;
                                    }
                                }
                            }

                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.FlexibleSpace();
                                if (GUILayout.Button("+ Add Parameter", GUILayout.Width(130)))
                                {
                                    e.parameters.Add(new EventParameterDefinition
                                    {
                                        parameterName = "parameter" + (e.parameters.Count + 1),
                                        parameterType = EventParameterType.String
                                    });
                                }
                            }

                            // Keep the legacy field synchronized so intentionally removing the last
                            // parameter does not cause the old single parameter to reappear.
                            e.parameterType = e.parameters.Count == 1
                                ? e.parameters[0].parameterType
                                : EventParameterType.None;
                        }
                    }

                    if (category.events.Count == 0)
                        EditorGUILayout.LabelField("No Events", EditorStyles.miniLabel);
                }
                EditorGUILayout.Space(4);
            }

            // Apply mutations only after the layout scopes have closed.
            foreach (var item in eventsToDelete)
                item.category.events.Remove(item.definition);

            if (!string.IsNullOrEmpty(categoryToDelete))
                database.categories.RemoveAll(x => x != null && x.categoryName == categoryToDelete);

            if (eventsToDelete.Count > 0 || !string.IsNullOrEmpty(categoryToDelete))
                EditorUtility.SetDirty(database);
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
        }

        private bool HasVisibleEvent(EventCategoryDefinition category)
        {
            return string.IsNullOrWhiteSpace(searchQuery) ||
                   GetVisibleEventCount(category) > 0;
        }

        private int GetVisibleEventCount(EventCategoryDefinition category)
        {
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

            if (database.categories.Any(x => x.categoryName == name))
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

            if (category.events.Any(x => x.eventName == name))
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

            foreach (EventCategoryDefinition category in database.categories)
            {
                if (category != null)
                    GenerateCategory(category);
            }

            GenerateEventManager();
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
            var categoryNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (EventCategoryDefinition category in database.categories)
            {
                if (category == null || !IsValidIdentifier(category.categoryName))
                    return "カテゴリ名がC#の識別子として不正です。";
                if (!categoryNames.Add(category.categoryName))
                    return $"カテゴリ名が重複しています: {category.categoryName}";

                var eventNames = new HashSet<string>(StringComparer.Ordinal);
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
                        if (parameter.parameterType == EventParameterType.None)
                            return $"{definition.eventName}.{parameter.parameterName} の型に None は指定できません。";
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
                existing.parameterType = type;
                existing.parameters = type == EventParameterType.None
                    ? new List<EventParameterDefinition>()
                    : new List<EventParameterDefinition> { new EventParameterDefinition { parameterName = "value", parameterType = type } };
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

        private void GenerateCategory(EventCategoryDefinition category)
        {
            string className = category.categoryName + "Events";
            string path = $"{GeneratedCategoriesFolder}/{className}.cs";
            StringBuilder sb = new();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by EventManagerGeneratorWindow.");
            sb.AppendLine("// Do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Cysharp.Threading.Tasks;");
            sb.AppendLine("using UniRx;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            foreach (EventDefinition e in category.events)
            {
                EnsureParameterList(e);
                if (e.parameters.Count > 1)
                {
                    string payload = GetPayloadTypeName(category, e);
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
            foreach (EventDefinition e in category.events)
            {
                EnsureParameterList(e);
                string type = GetEventPayloadType(category, e);
                sb.AppendLine($"    private readonly Subject<{type}> _{e.eventName} = new();");
            }

            if (category.categoryName == "UI")
            {
                sb.AppendLine("#if UNI7_UI");
                sb.AppendLine("    private readonly Action<string> _hubOpenWindow;");
                sb.AppendLine("    private readonly Action<string> _hubOpenModal;");
                sb.AppendLine("    private readonly Action<string> _hubCloseUI;");
                sb.AppendLine("    private readonly Action _hubCloseTopModal;");
                sb.AppendLine("    private readonly Action _hubCloseAll;");
                sb.AppendLine("    public UIEvents()");
                sb.AppendLine("    {");
                sb.AppendLine("        _hubOpenWindow = value => _OpenWindow.OnNext(value);");
                sb.AppendLine("        _hubOpenModal = value => _OpenModal.OnNext(value);");
                sb.AppendLine("        _hubCloseUI = value => _CloseUI.OnNext(value);");
                sb.AppendLine("        _hubCloseTopModal = () => _CloseTopModal.OnNext(Unit.Default);");
                sb.AppendLine("        _hubCloseAll = () => _CloseAll.OnNext(Unit.Default);");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenWindow += _hubOpenWindow;");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenModal += _hubOpenModal;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseUI += _hubCloseUI;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseTopModal += _hubCloseTopModal;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseAll += _hubCloseAll;");
                sb.AppendLine("    }");
                sb.AppendLine("#endif");
            }

            foreach (EventDefinition e in category.events)
                GenerateEventMethods(sb, e, category.categoryName);

            sb.AppendLine("    public void Dispose()");
            sb.AppendLine("    {");
            if (category.categoryName == "UI")
            {
                sb.AppendLine("#if UNI7_UI");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenWindow -= _hubOpenWindow;");
                sb.AppendLine("        Uni7.UI.UIEventHub.OpenModal -= _hubOpenModal;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseUI -= _hubCloseUI;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseTopModal -= _hubCloseTopModal;");
                sb.AppendLine("        Uni7.UI.UIEventHub.CloseAll -= _hubCloseAll;");
                sb.AppendLine("#endif");
            }
            foreach (EventDefinition e in category.events)
            {
                sb.AppendLine($"        _{e.eventName}.OnCompleted();");
                sb.AppendLine($"        _{e.eventName}.Dispose();");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
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
                sb.AppendLine($"    public UniTask Wait{e.eventName}() => _{e.eventName}.First().ToUniTask();");
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
                sb.AppendLine($"    public UniTask<{type}> Wait{e.eventName}() => _{e.eventName}.First().ToUniTask();");
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
                sb.AppendLine($"    public UniTask<{payloadType}> Wait{e.eventName}() => _{e.eventName}.First().ToUniTask();");
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
            => categoryName + definition.eventName + "Payload";

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Value";
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private void GenerateEventManager()
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
            sb.AppendLine();
            sb.AppendLine("    public static EventManager Instance => _instance ??= new EventManager();");
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

            foreach (EventCategoryDefinition category in database.categories)
            {
                string fieldName = ToCamelCase(category.categoryName);

                sb.AppendLine($"        _{fieldName}.Dispose();");
            }

            sb.AppendLine("        _instance = null;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(EventManagerPath, sb.ToString(), new UTF8Encoding(false));
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