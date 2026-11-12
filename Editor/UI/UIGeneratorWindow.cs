#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Uni7.UI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Uni7.Editor.UI
{
    /// <summary>
    /// Compact UI registry editor. Basic workflow only needs ID + Type.
    /// Advanced settings are hidden per entry. Existing prefabs are never overwritten.
    /// </summary>
    public sealed class UIGeneratorWindow : EditorWindow
    {
        private const string DefaultPrefabRoot = "Assets/Uni7/UI/Prefabs";
        private const string DefaultRegistryPath = "Assets/Resources/Uni7/UIRegistry.asset";
        private const string RuntimeResourcesKey = "Uni7/UIRegistry";

        private UIRegistry registry;
        private string newId = "";
        private UIType newType = UIType.Window;
        private string search = "";
        private bool showHelp;
        private bool showGlobalOptions;
        private string prefabRoot = DefaultPrefabRoot;
        private int defaultInitialPoolSize = 0;
        private int defaultMaxPoolSize = 2;
        private float defaultAnimationDuration = 0.18f;
        private bool defaultScaleAnimation = true;
        private Vector2 listScroll;
        private string status = "";
        private MessageType statusType = MessageType.Info;

        [MenuItem("Tools/Uni7/UI/Generator", priority = 220)]
        public static void Open()
        {
            UIGeneratorWindow window = GetWindow<UIGeneratorWindow>("UI Generator");
            window.minSize = new Vector2(430f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("UI Generator");
            LoadRegistry();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (registry == null)
            {
                EditorGUILayout.HelpBox("UIRegistryを読み込めません。Reloadを押すか、Registryを作成してください。", MessageType.Warning);
                if (GUILayout.Button("Create Registry")) CreateRegistry();
                return;
            }

            DrawCreateSection();
            DrawRegistryHeader();
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            DrawEntries();
            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Uni7 / UI", EditorStyles.boldLabel, GUILayout.Width(90));
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(48)))
                    LoadRegistry();
                if (GUILayout.Button("Select Registry", EditorStyles.toolbarButton, GUILayout.Width(95)) && registry != null)
                    Selection.activeObject = registry;
                GUILayout.FlexibleSpace();
                search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(100), GUILayout.MaxWidth(220));
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) search = "";
                showHelp = GUILayout.Toggle(showHelp, "Help", EditorStyles.toolbarButton, GUILayout.Width(42));
            }

            if (showHelp)
            {
                EditorGUILayout.HelpBox(
                    "基本操作は UI ID と種類を入力して登録するだけです。詳細設定は各項目の More Options にあります。\n" +
                    "Prefabが既に存在する場合は再生成・上書きしません。Scene上のUIルートは実行時に自動生成されます。",
                    MessageType.Info);
            }
        }

        private void DrawCreateSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("新しいUIを登録", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newId = EditorGUILayout.TextField(new GUIContent("ID", "EventManagerから指定する一意のID"), newId);
                newType = (UIType)EditorGUILayout.EnumPopup(newType, GUILayout.Width(90));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newId)))
                {
                    if (GUILayout.Button("Prefab作成 + ID登録", GUILayout.Height(28), GUILayout.Width(150)))
                        CreateAndRegister();
                }
            }

            showGlobalOptions = EditorGUILayout.Foldout(showGlobalOptions, "More Options (default settings)", true);
            if (showGlobalOptions)
            {
                EditorGUI.indentLevel++;
                prefabRoot = EditorGUILayout.TextField("Prefab Folder", prefabRoot);
                defaultInitialPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Initial Pool", defaultInitialPoolSize));
                defaultMaxPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Max Pool", defaultMaxPoolSize));
                defaultAnimationDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Animation Duration", defaultAnimationDuration));
                defaultScaleAnimation = EditorGUILayout.Toggle("Scale Animation", defaultScaleAnimation);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRegistryHeader()
        {
            int total = registry.Entries.Count;
            int windows = registry.Entries.Count(x => x != null && x.type == UIType.Window);
            int modals = total - windows;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"登録済み {total}   Window {windows}   Modal {modals}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save", GUILayout.Width(60))) SaveRegistry();
            }
        }

        private void DrawEntries()
        {
            if (registry.EditorEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("まだUIが登録されていません。上のIDと種類を指定して追加してください。", MessageType.Info);
                return;
            }

            for (int i = 0; i < registry.EditorEntries.Count; i++)
            {
                UIEntry entry = registry.EditorEntries[i];
                if (entry == null) continue;
                if (!MatchesSearch(entry)) continue;

                string key = string.IsNullOrEmpty(entry.id) ? $"Entry_{i}" : entry.id;
                EditorGUILayout.BeginVertical("box");
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(entry.type == UIType.Window ? "W" : "M", EditorStyles.miniButton, GUILayout.Width(24));
                    EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DrawPrefabStatus(entry);
                    if (GUILayout.Button("Select", GUILayout.Width(48)))
                    {
                        string path = GetPrefabPath(entry);
                        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (asset != null) Selection.activeObject = asset;
                        else SetStatus("Prefabが見つかりません: " + path, MessageType.Warning);
                    }
                    if (GUILayout.Button("×", GUILayout.Width(24)))
                    {
                        if (EditorUtility.DisplayDialog("UI IDを削除", $"'{entry.id}' のRegistry登録だけを削除します。\nPrefabファイルは削除しません。", "登録削除", "キャンセル"))
                        {
                            registry.EditorEntries.RemoveAt(i);
                            SaveRegistry();
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                bool expanded = EditorPrefs.GetBool("Uni7.UIGenerator.Details." + key, false);
                bool next = EditorGUILayout.Foldout(expanded, "More Options", true);
                if (next != expanded) EditorPrefs.SetBool("Uni7.UIGenerator.Details." + key, next);
                if (next)
                {
                    EditorGUI.indentLevel++;
                    entry.address = EditorGUILayout.TextField(new GUIContent("Addressables Address", "通常はIDと同じ値"), entry.address);
                    entry.initialPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Initial Pool", entry.initialPoolSize));
                    entry.maxPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Max Pool", entry.maxPoolSize));
                    entry.animationDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Animation Duration", entry.animationDuration));
                    entry.useScaleAnimation = EditorGUILayout.Toggle("Scale Animation", entry.useScaleAnimation);
                    EditorGUILayout.LabelField("Prefab", GetPrefabPath(entry), EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("不足Prefabを作成"))
                            CreateMissingPrefab(entry);
                        if (GUILayout.Button("Addressablesを確認"))
                            EnsureAddressable(entry, false);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawPrefabStatus(UIEntry entry)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(GetPrefabPath(entry)) != null;
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = exists ? new Color(0.25f, 0.72f, 0.35f) : new Color(0.85f, 0.58f, 0.2f);
            GUILayout.Label(exists ? "Prefab OK" : "Prefabなし", style, GUILayout.Width(70));
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, statusType);
            EditorGUILayout.LabelField("Registry: " + AssetDatabase.GetAssetPath(registry), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Runtime: Resources/" + RuntimeResourcesKey + " を自動読み込みします。", EditorStyles.miniLabel);
        }

        private bool MatchesSearch(UIEntry entry)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return (entry.id ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (entry.address ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.type.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CreateAndRegister()
        {
            string id = newId.Trim();
            if (!IsValidId(id))
            {
                SetStatus("IDは英字または_で始まり、英数字と_のみ使用できます。", MessageType.Warning);
                return;
            }
            if (registry.TryGet(id, out _))
            {
                SetStatus($"ID '{id}' は既に登録されています。既存項目を一覧から確認してください。", MessageType.Warning);
                return;
            }

            UIEntry entry = new UIEntry
            {
                id = id,
                address = id,
                type = newType,
                initialPoolSize = defaultInitialPoolSize,
                maxPoolSize = defaultMaxPoolSize,
                animationDuration = defaultAnimationDuration,
                useScaleAnimation = defaultScaleAnimation
            };

            if (!EnsurePrefabExists(entry, out string prefabPath))
                return;

            if (!EnsureAddressable(entry, true))
                return;

            registry.EditorEntries.Add(entry);
            SaveRegistry();
            newId = "";
            SetStatus($"登録しました: {id} ({entry.type}) / {prefabPath}", MessageType.Info);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        private void CreateMissingPrefab(UIEntry entry)
        {
            if (EnsurePrefabExists(entry, out string path))
            {
                if (EnsureAddressable(entry, true))
                    SetStatus("Prefabを確認しました: " + path, MessageType.Info);
            }
        }

        private bool EnsurePrefabExists(UIEntry entry, out string prefabPath)
        {
            prefabPath = GetPrefabPath(entry);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                bool valid = entry.type == UIType.Window
                    ? existing.GetComponent<UIWindow>() != null
                    : existing.GetComponent<UIModal>() != null;
                if (!valid)
                {
                    SetStatus($"同名Prefabは存在しますが、{entry.type}用コンポーネントがありません。上書きはしません: {prefabPath}", MessageType.Error);
                    return false;
                }
                return true;
            }

            // AssetDatabaseに未反映でも、同名ファイルが存在するなら絶対に上書きしない。
            if (File.Exists(prefabPath))
            {
                SetStatus($"同名ファイルが存在しますがPrefabとして読み込めません。安全のため上書きしません: {prefabPath}", MessageType.Error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(prefabRoot) || !prefabRoot.StartsWith("Assets/", StringComparison.Ordinal) || prefabRoot.Contains(".."))
            {
                SetStatus("Prefab FolderはAssets配下を指定してください。", MessageType.Error);
                return false;
            }

            EnsureFolder(Path.GetDirectoryName(prefabPath).Replace("\\", "/"));
            GameObject template = CreateTemplate(entry);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, prefabPath);
            DestroyImmediate(template);
            if (saved == null)
            {
                SetStatus("Prefabの作成に失敗しました: " + prefabPath, MessageType.Error);
                return false;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        private GameObject CreateTemplate(UIEntry entry)
        {
            GameObject root = new GameObject(entry.id, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (entry.type == UIType.Window)
            {
                root.GetComponent<Image>().color = new Color(0.09f, 0.11f, 0.15f, 0.96f);
                root.AddComponent<UIWindowView>();
                CreateContentRoot(root.transform, "Content", new Vector2(24f, 24f), new Vector2(-24f, -24f));
            }
            else
            {
                // Modal root covers the screen for backdrop/raycast handling.
                root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.48f);
                root.AddComponent<UIModalView>();

                GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(root.transform, false);
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(560f, 320f);
                panel.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.23f, 1f);
                CreateContentRoot(panel.transform, "Content", new Vector2(24f, 24f), new Vector2(-24f, -24f));
            }

            root.GetComponent<Image>().raycastTarget = true;
            return root;
        }

        private static void CreateContentRoot(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject content = new GameObject(name, typeof(RectTransform));
            content.transform.SetParent(parent, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = offsetMin;
            contentRect.offsetMax = offsetMax;
        }

        private bool EnsureAddressable(UIEntry entry, bool showErrors)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                if (showErrors) SetStatus("Addressables Settingsがありません。Addressables Groupsを初期化してからAddress登録してください。", MessageType.Warning);
                return !showErrors;
            }

            string path = GetPrefabPath(entry);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return false;

            AddressableAssetEntry existingForAsset = settings.FindAssetEntry(guid);
            if (existingForAsset != null)
            {
                if (existingForAsset.address == entry.address) return true;
                if (showErrors)
                    SetStatus($"Prefabは既にAddressables登録済みですが、Address '{existingForAsset.address}' を保持しました。既存Addressを上書きしません。", MessageType.Warning);
                return false;
            }

            AddressableAssetEntry addressConflict = settings.groups
                .Where(g => g != null)
                .SelectMany(g => g.entries)
                .FirstOrDefault(x => x != null && x.address == entry.address);
            if (addressConflict != null)
            {
                if (showErrors) SetStatus($"Address '{entry.address}' は別アセットで使用中です。登録を中止しました。", MessageType.Error);
                return false;
            }

            AddressableAssetGroup group = settings.DefaultGroup;
            if (group == null)
            {
                if (showErrors) SetStatus("AddressablesのDefault Groupがありません。", MessageType.Error);
                return false;
            }

            AddressableAssetEntry created = settings.CreateOrMoveEntry(guid, group);
            created.address = entry.address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryCreated, created, true);
            return true;
        }

        private string GetPrefabPath(UIEntry entry)
        {
            string kindFolder = entry.type == UIType.Window ? "Windows" : "Modals";
            return $"{prefabRoot.TrimEnd('/')}/{kindFolder}/{SanitizeFileName(entry.id)}.prefab";
        }

        private void LoadRegistry()
        {
            registry = AssetDatabase.LoadAssetAtPath<UIRegistry>(DefaultRegistryPath);
            if (registry == null)
                SetStatus("Registryがありません。Create Registryで作成できます。", MessageType.Warning);
            else
                SetStatus("Registryを読み込みました。", MessageType.Info);
        }

        private void CreateRegistry()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Uni7");
            if (AssetDatabase.LoadAssetAtPath<UIRegistry>(DefaultRegistryPath) != null)
            {
                LoadRegistry();
                return;
            }
            registry = CreateInstance<UIRegistry>();
            AssetDatabase.CreateAsset(registry, DefaultRegistryPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SetStatus("UIRegistryを作成しました。", MessageType.Info);
        }

        private void SaveRegistry()
        {
            if (registry == null) return;
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }

        private static bool IsValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
            return value.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folder = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif
