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

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawEventList();
            EditorGUILayout.EndScrollView();

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
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Add Event Definition", EditorStyles.boldLabel);

            DrawAddCategory();
            EditorGUILayout.Space(4);
            DrawAddEvent();

            EditorGUILayout.EndVertical();
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
            for (int categoryIndex = 0; categoryIndex < database.categories.Count; categoryIndex++)
            {
                EventCategoryDefinition category = database.categories[categoryIndex];

                if (!HasVisibleEvent(category))
                    continue;

                if (!categoryFoldouts.ContainsKey(category.categoryName))
                    categoryFoldouts[category.categoryName] = true;

                EditorGUILayout.BeginVertical("box");

                using (new EditorGUILayout.HorizontalScope())
                {
                    categoryFoldouts[category.categoryName] =
                        EditorGUILayout.Foldout(
                            categoryFoldouts[category.categoryName],
                            $"{category.categoryName}  ({GetVisibleEventCount(category)}/{category.events.Count})",
                            true,
                            EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Delete Category", GUILayout.Width(120)))
                    {
                        if (EditorUtility.DisplayDialog(
                            "Delete Category",
                            $"「{category.categoryName}」を削除しますか？",
                            "Delete",
                            "Cancel"))
                        {
                            database.categories.RemoveAt(categoryIndex);
                            EditorUtility.SetDirty(database);
                            EditorGUILayout.EndVertical();
                            break;
                        }
                    }
                }

                if (categoryFoldouts[category.categoryName])
                {
                    for (int i = 0; i < category.events.Count; i++)
                    {
                        EventDefinition e = category.events[i];

                        if (!MatchesFilter(category, e))
                            continue;

                        EditorGUILayout.BeginVertical("helpbox");

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            e.eventName = EditorGUILayout.TextField("Event", e.eventName);

                            e.parameterType =
                                (EventParameterType)EditorGUILayout.EnumPopup(
                                    e.parameterType,
                                    GUILayout.Width(110));

                            if (GUILayout.Button("Delete", GUILayout.Width(70)))
                            {
                                category.events.RemoveAt(i);
                                EditorUtility.SetDirty(database);
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.EndVertical();
                                break;
                            }
                        }

                        e.comment = EditorGUILayout.TextArea(
                            e.comment ?? "",
                            GUILayout.MinHeight(45));

                        EditorGUILayout.LabelField(
                            "Comment",
                            EditorStyles.miniLabel);

                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(2);
                    }

                    if (category.events.Count == 0)
                    {
                        EditorGUILayout.LabelField(
                            "No Events",
                            EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
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
            return category.categoryName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   e.eventName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
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

            category.events.Add(new EventDefinition
            {
                eventName = name,
                comment = "",
                parameterType = newParameterType
            });

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

            // EnsureFolder("Assets/EventSystem");
            EnsureFolder(GeneratedFolder);
            EnsureFolder(GeneratedCategoriesFolder);

            // Delete old generated category files.
            string[] oldFiles = AssetDatabase.FindAssets("t:MonoScript", new[] { GeneratedCategoriesFolder });

            foreach (string guid in oldFiles)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.EndsWith("Events.cs", StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            foreach (EventCategoryDefinition category in database.categories)
            {
                GenerateCategory(category);
            }

            GenerateEventManager();

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Event Manager",
                "イベントコードの生成が完了しました。",
                "OK");
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
            sb.AppendLine($"public sealed class {className} : IDisposable");
            sb.AppendLine("{");

            foreach (EventDefinition e in category.events)
            {
                string type = GetParameterType(e.parameterType);
                sb.AppendLine($"    private readonly Subject<{type}> _{e.eventName} = new();");
            }

            sb.AppendLine();

            foreach (EventDefinition e in category.events)
            {
                GenerateEventMethods(sb, e);
            }

            sb.AppendLine("    public void Dispose()");
            sb.AppendLine("    {");

            foreach (EventDefinition e in category.events)
            {
                sb.AppendLine($"        _{e.eventName}.OnCompleted();");
                sb.AppendLine($"        _{e.eventName}.Dispose();");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private void GenerateEventMethods(StringBuilder sb, EventDefinition e)
        {
            string type = GetParameterType(e.parameterType);
            bool noParameter = e.parameterType == EventParameterType.None;

            AppendSummary(sb, e.comment);
            sb.AppendLine($"    // Event: {e.eventName}");
            sb.AppendLine();

            if (noParameter)
            {
                sb.AppendLine($"    public IDisposable On{e.eventName}(Action callback)");
                sb.AppendLine("    {");
                sb.AppendLine($"        return _{e.eventName}.Subscribe(_ => callback());");
                sb.AppendLine("    }");
                sb.AppendLine();

                sb.AppendLine($"    public UniTask Wait{e.eventName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        return _{e.eventName}.First().ToUniTask();");
                sb.AppendLine("    }");
                sb.AppendLine();

                sb.AppendLine($"    public void Emit{e.eventName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        _{e.eventName}.OnNext(Unit.Default);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"    public IDisposable On{e.eventName}(Action<{type}> callback)");
                sb.AppendLine("    {");
                sb.AppendLine($"        return _{e.eventName}.Subscribe(callback);");
                sb.AppendLine("    }");
                sb.AppendLine();

                sb.AppendLine($"    public UniTask<{type}> Wait{e.eventName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        return _{e.eventName}.First().ToUniTask();");
                sb.AppendLine("    }");
                sb.AppendLine();

                sb.AppendLine($"    public void Emit{e.eventName}({type} value)");
                sb.AppendLine("    {");
                sb.AppendLine($"        _{e.eventName}.OnNext(value);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
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
#endif

}