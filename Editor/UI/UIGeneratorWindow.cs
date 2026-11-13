#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Uni7.UI;
using Uni7.Events;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Uni7.Editor.UI
{
    /// <summary>
    /// Uni7 UI workspace. Registry data is edited here; prefab assets are only created when missing
    /// and are never silently replaced.
    /// </summary>
    public sealed class UIGeneratorWindow : EditorWindow
    {
        private const string RegistryPath = "Assets/Resources/Uni7/UIRegistry.asset";
        private const string ButtonRegistryPath = "Assets/Resources/Uni7/UIButtonRegistry.asset";
        private const string EventDatabasePath = "Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset";
        private const string DefaultPrefabRoot = "Assets/Uni7/UI/Prefabs";
        private const string DefaultButtonPrefabRoot = "Assets/Uni7/UI/Buttons";

        private enum WorkspaceTab { UI, Button, Settings }
        private WorkspaceTab tab;
        private UIRegistry registry;
        private UIButtonRegistry buttonRegistry;
        private EventDefinitionDatabase eventDatabase;

        private string search = "";
        private int uiFilter;
        private int buttonFilter;
        private Vector2 listScroll;
        private string status = "";
        private MessageType statusType = MessageType.Info;
        private bool showHelp;
        private bool showCreateOptions;

        private enum ButtonPlacementMode { CurrentPrefabStage, SpecificPrefab }
        private ButtonPlacementMode buttonPlacementMode = ButtonPlacementMode.CurrentPrefabStage;
        private GameObject buttonTargetPrefab;
        private string buttonTargetChildPath = "Content";

        private string newUIId = "";
        private UIType newUIType = UIType.Window;
        private string newButtonId = "";
        private UIButtonAction newButtonAction = UIButtonAction.OpenWindow;

        private string prefabRoot = DefaultPrefabRoot;
        private string buttonPrefabRoot = DefaultButtonPrefabRoot;
        private int defaultInitialPoolSize;
        private int defaultMaxPoolSize = 2;
        private float defaultAnimationDuration = 0.18f;
        private bool defaultScaleAnimation = true;

        [MenuItem("Tools/Uni7/UI/Generator", priority = 220)]
        public static void Open()
        {
            UIGeneratorWindow window = GetWindow<UIGeneratorWindow>("Uni7 Workspace");
            window.minSize = new Vector2(570f, 500f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Uni7 Workspace");
            LoadAssets();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawTabs();
            if (showHelp)
                EditorGUILayout.HelpBox("基本操作はIDと種類（またはAction）だけ。詳細設定は各項目のMore Optionsにあります。Prefabは自動上書きしません。", MessageType.Info);

            switch (tab)
            {
                case WorkspaceTab.UI: DrawUITab(); break;
                case WorkspaceTab.Button: DrawButtonTab(); break;
                case WorkspaceTab.Settings: DrawSettingsTab(); break;
            }
            DrawStatus();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("UNI7", EditorStyles.boldLabel, GUILayout.Width(42));
                GUILayout.Label("Workspace", EditorStyles.toolbarButton, GUILayout.Width(72));
                GUILayout.FlexibleSpace();
                search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField,
                    GUILayout.MinWidth(100), GUILayout.MaxWidth(240));
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) search = "";
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(48))) LoadAssets();
                showHelp = GUILayout.Toggle(showHelp, "?", EditorStyles.toolbarButton, GUILayout.Width(24));
            }
        }

        private void DrawTabs()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTabButton("UI", WorkspaceTab.UI);
                DrawTabButton("Button", WorkspaceTab.Button);
                DrawTabButton("Settings", WorkspaceTab.Settings);
            }
            EditorGUILayout.Space(5);
        }

        private void DrawTabButton(string label, WorkspaceTab value)
        {
            bool selected = tab == value;
            Color old = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.55f, 0.78f, 1f);
            if (GUILayout.Button(label, GUILayout.Height(28))) tab = value;
            GUI.backgroundColor = old;
        }

        // ---------------------------------------------------------------------
        // UI TAB
        // ---------------------------------------------------------------------
        private void DrawUITab()
        {
            if (registry == null)
            {
                EditorGUILayout.HelpBox("UIRegistryがありません。作成するとUIの登録を開始できます。", MessageType.Warning);
                if (GUILayout.Button("UIRegistryを作成", GUILayout.Height(30))) CreateUIRegistry();
                return;
            }

            DrawCreateUI();
            DrawUIHeader();
            using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll))
            {
                listScroll = scroll.scrollPosition;
                for (int i = 0; i < registry.EditorEntries.Count; i++)
                {
                    UIEntry entry = registry.EditorEntries[i];
                    if (entry == null || !MatchesUI(entry)) continue;
                    DrawUIEntry(entry, i);
                }
            }
        }

        private void DrawCreateUI()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
            EditorGUILayout.LabelField("UIを追加", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newUIId = EditorGUILayout.TextField("ID", newUIId);
                newUIType = (UIType)EditorGUILayout.EnumPopup(newUIType, GUILayout.Width(105));
                using (new EditorGUI.DisabledScope(!IsValidId(newUIId) || registry.EditorEntries.Any(x => x != null && x.id == newUIId)))
                    if (GUILayout.Button("追加", GUILayout.Width(72), GUILayout.Height(22))) CreateAndRegisterUI();
            }
            showCreateOptions = EditorGUILayout.Foldout(showCreateOptions, "More Options", true);
            if (showCreateOptions)
            {
                EditorGUI.indentLevel++;
                defaultInitialPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Initial Pool", defaultInitialPoolSize));
                defaultMaxPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Max Pool", defaultMaxPoolSize));
                defaultAnimationDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Animation Duration", defaultAnimationDuration));
                defaultScaleAnimation = EditorGUILayout.Toggle("Scale Animation", defaultScaleAnimation);
                EditorGUI.indentLevel--;
            }
            }
        }

        private void DrawUIHeader()
        {
            int total = registry.Entries.Count;
            int windows = registry.Entries.Count(x => x != null && x.type == UIType.Window);
            int modals = registry.Entries.Count(x => x != null && x.type == UIType.Modal);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"登録 {total}  /  Window {windows}  /  Modal {modals}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                uiFilter = EditorGUILayout.Popup(uiFilter, new[] { "All", "Window", "Modal" }, GUILayout.Width(90));
                if (GUILayout.Button("Validate", GUILayout.Width(62))) ValidateUIEntries();
                if (GUILayout.Button("Save", GUILayout.Width(55))) Save(registry);
            }
        }

        private void DrawUIEntry(UIEntry entry, int index)
        {
            string path = GetPrefabPath(entry);
            PrefabHealth health = GetPrefabHealth(path, entry.prefabGuid, entry.prefabDependencyHash);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawAssetPreview(path);
                GUILayout.Label(entry.type == UIType.Window ? "W" : "M", EditorStyles.miniButton, GUILayout.Width(24));
                EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DrawHealthLabel(health);
                if (GUILayout.Button("Select", GUILayout.Width(48))) SelectAsset(path);
                if (GUILayout.Button("×", GUILayout.Width(24)) && EditorUtility.DisplayDialog("登録解除", $"'{entry.id}' のRegistry登録だけを解除します。Prefabは削除しません。", "解除", "キャンセル"))
                {
                    registry.EditorEntries.RemoveAt(index);
                    Save(registry);
                    GUIUtility.ExitGUI();
                }
            }

            string foldoutKey = "Uni7.UI.More." + entry.id;
            bool expanded = EditorPrefs.GetBool(foldoutKey, false);
            bool next = EditorGUILayout.Foldout(expanded, "More Options", true);
            if (next != expanded) EditorPrefs.SetBool(foldoutKey, next);
            if (next)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                entry.address = EditorGUILayout.TextField("Addressables Address", entry.address);
                entry.initialPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Initial Pool", entry.initialPoolSize));
                entry.maxPoolSize = Mathf.Max(0, EditorGUILayout.IntField("Max Pool", entry.maxPoolSize));
                entry.animationDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Animation Duration", entry.animationDuration));
                entry.useScaleAnimation = EditorGUILayout.Toggle("Scale Animation", entry.useScaleAnimation);
                GameObject currentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                GameObject selectedPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab Asset", currentPrefab, typeof(GameObject), false);
                if (selectedPrefab != currentPrefab)
                {
                    string selectedPath = selectedPrefab != null ? AssetDatabase.GetAssetPath(selectedPrefab) : "";
                    Type expectedType = entry.type == UIType.Window ? typeof(UIWindow) : typeof(UIModal);
                    if (selectedPrefab != null && selectedPrefab.GetComponent(expectedType) == null)
                        SetStatus($"選択Prefabに{expectedType.Name}がありません。", MessageType.Error);
                    else
                    {
                        entry.prefabPath = selectedPath;
                        entry.prefabGuid = selectedPrefab != null ? AssetDatabase.AssetPathToGUID(selectedPath) : "";
                        entry.prefabDependencyHash = selectedPrefab != null ? AssetDatabase.GetAssetDependencyHash(selectedPath).ToString() : "";
                    }
                }
                EditorGUILayout.LabelField("Prefab Path", GetPrefabPath(entry), EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck()) Save(registry);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("不足Prefabを作成")) CreateMissingUIPrefab(entry);
                    if (GUILayout.Button("Addressables確認")) EnsureAddressable(entry, true);
                    if (GUILayout.Button("Addressを更新")) UpdateAddressableAddress(entry);
                    if (health == PrefabHealth.Changed || health == PrefabHealth.Unknown)
                        if (GUILayout.Button("現在のPrefabを基準にする")) RecordPrefabBaseline(entry, path);
                }
                EditorGUI.indentLevel--;
            }
            }
        }

        private bool MatchesUI(UIEntry entry)
        {
            if (uiFilter == 1 && entry.type != UIType.Window) return false;
            if (uiFilter == 2 && entry.type != UIType.Modal) return false;
            return string.IsNullOrWhiteSpace(search) || (!string.IsNullOrEmpty(entry.id) && entry.id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void CreateAndRegisterUI()
        {
            string id = newUIId.Trim();
            if (!IsValidId(id)) { SetStatus("IDは英字または_で始まる英数字と_にしてください。", MessageType.Error); return; }
            if (registry.TryGet(id, out _)) { SetStatus("同じUI IDが既に登録されています。", MessageType.Error); return; }

            AddressableAssetSettings currentSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (currentSettings == null || currentSettings.DefaultGroup == null)
            {
                SetStatus("先にAddressables SettingsとDefault Groupを初期化してください。Prefab作成前に停止しました。", MessageType.Error);
                return;
            }
            string kindFolder = newUIType == UIType.Window ? "Windows" : "Modals";
            string candidatePath = $"{prefabRoot.TrimEnd('/')}/{kindFolder}/{SanitizeFileName(id)}.prefab";
            string candidateGuid = AssetDatabase.AssetPathToGUID(candidatePath);
            AddressableAssetEntry currentPrefabEntry = !string.IsNullOrEmpty(candidateGuid) ? currentSettings.FindAssetEntry(candidateGuid) : null;
            if (currentPrefabEntry != null && currentPrefabEntry.address != id)
            {
                SetStatus($"このPrefabは既にAddress '{currentPrefabEntry.address}' で登録されています。既存Addressを自動変更しません。", MessageType.Error);
                return;
            }
            bool addressUsed = currentSettings.groups.Where(g => g != null).SelectMany(g => g.entries)
                .Any(x => x != null && x.address == id && x.guid != candidateGuid);
            if (addressUsed)
            {
                SetStatus($"Address '{id}' は既に使用されています。別のIDにしてください。", MessageType.Error);
                return;
            }

            UIEntry entry = new UIEntry
            {
                id = id, address = id, type = newUIType,
                initialPoolSize = defaultInitialPoolSize,
                maxPoolSize = defaultMaxPoolSize,
                animationDuration = defaultAnimationDuration,
                useScaleAnimation = defaultScaleAnimation
            };
            if (!EnsureUIPrefab(entry, out string path)) return;
            if (!EnsureAddressable(entry, true))
            {
                SetStatus("Addressables登録を確認できませんでした。UIはRegistryに追加していません。", MessageType.Error);
                return;
            }
            registry.EditorEntries.Add(entry);
            Save(registry);
            newUIId = "";
            SetStatus($"{id} を登録しました。Prefabは新規作成し、既存ファイルは変更していません。", MessageType.Info);
            SelectAsset(path);
        }

        private void CreateMissingUIPrefab(UIEntry entry)
        {
            if (EnsureUIPrefab(entry, out string path))
            {
                EnsureAddressable(entry, true);
                Save(registry);
                SetStatus("Prefabを確認しました: " + path, MessageType.Info);
            }
        }

        private bool EnsureUIPrefab(UIEntry entry, out string path)
        {
            path = GetPrefabPath(entry);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Type expected = entry.type == UIType.Window ? typeof(UIWindow) : typeof(UIModal);
                if (existing.GetComponent(expected) == null)
                {
                    SetStatus($"同じパスにPrefabがありますが{expected.Name}がありません。上書きしません: {path}", MessageType.Error);
                    return false;
                }
                if (string.IsNullOrEmpty(entry.prefabPath)) entry.prefabPath = path;
                if (string.IsNullOrEmpty(entry.prefabGuid)) RecordPrefabBaseline(entry, path);
                return true;
            }
            if (File.Exists(path))
            {
                SetStatus("同名ファイルが存在します。安全のため上書きしません: " + path, MessageType.Error);
                return false;
            }
            if (!IsSafeAssetFolder(prefabRoot)) { SetStatus("Prefab FolderはAssets配下を指定してください。", MessageType.Error); return false; }

            EnsureFolder(Path.GetDirectoryName(path).Replace("\\", "/"));
            GameObject template = CreateUITemplate(entry);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, path);
            DestroyImmediate(template);
            if (saved == null) { SetStatus("Prefab作成に失敗しました: " + path, MessageType.Error); return false; }
            entry.prefabPath = path;
            RecordPrefabBaseline(entry, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        private static GameObject CreateUITemplate(UIEntry entry)
        {
            GameObject root = new GameObject(entry.id, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            if (entry.type == UIType.Window)
            {
                root.GetComponent<Image>().color = new Color(0.09f, 0.11f, 0.15f, 0.96f);
                root.AddComponent<UIWindowView>();
                CreateContentRoot(root.transform, "Content", new Vector2(24f, 24f), new Vector2(-24f, -24f));
            }
            else
            {
                root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.48f);
                root.AddComponent<UIModalView>();
                GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(root.transform, false);
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f); panelRect.sizeDelta = new Vector2(560f, 320f);
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
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }

        // ---------------------------------------------------------------------
        // BUTTON TAB
        // ---------------------------------------------------------------------
        private void DrawButtonTab()
        {
            if (buttonRegistry == null)
            {
                EditorGUILayout.HelpBox("ButtonRegistryがありません。Button設定を保存するために作成します。", MessageType.Info);
                if (GUILayout.Button("ButtonRegistryを作成", GUILayout.Height(30))) CreateButtonRegistry();
                return;
            }
            DrawCreateButton();
            DrawButtonPlacementSettings();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"登録済み {buttonRegistry.Entries.Count}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                buttonFilter = EditorGUILayout.Popup(buttonFilter, new[] { "All", "UI Action", "Custom Event" }, GUILayout.Width(115));
                if (GUILayout.Button("Validate", GUILayout.Width(62))) ValidateButtonEntries();
                if (GUILayout.Button("Save", GUILayout.Width(55))) Save(buttonRegistry);
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll))
            {
                listScroll = scroll.scrollPosition;
                for (int i = 0; i < buttonRegistry.EditorEntries.Count; i++)
                {
                    UIButtonEntry entry = buttonRegistry.EditorEntries[i];
                    if (entry == null || !MatchesButton(entry)) continue;
                    DrawButtonEntry(entry, i);
                }
            }
        }

        private void DrawCreateButton()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
            EditorGUILayout.LabelField("Buttonを追加", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newButtonId = EditorGUILayout.TextField("ID", newButtonId);
                newButtonAction = (UIButtonAction)EditorGUILayout.EnumPopup(newButtonAction, GUILayout.Width(145));
                using (new EditorGUI.DisabledScope(!IsValidId(newButtonId) || buttonRegistry.EditorEntries.Any(x => x != null && x.id == newButtonId)))
                    if (GUILayout.Button("追加", GUILayout.Width(72))) CreateButtonEntry();
            }
            }
        }

        private void DrawButtonPlacementSettings()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                EditorGUILayout.LabelField("Buttonの配置先", EditorStyles.boldLabel);
                buttonPlacementMode = (ButtonPlacementMode)EditorGUILayout.EnumPopup("Target", buttonPlacementMode);
                if (buttonPlacementMode == ButtonPlacementMode.CurrentPrefabStage)
                {
                    PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage == null)
                        EditorGUILayout.HelpBox("Prefab ModeでWindowなどのPrefabを開くと、そのPrefab内に配置できます。", MessageType.Info);
                    else
                        EditorGUILayout.LabelField("Open Prefab", stage.assetPath, EditorStyles.miniLabel);
                }
                else
                {
                    buttonTargetPrefab = (GameObject)EditorGUILayout.ObjectField("Target Prefab", buttonTargetPrefab, typeof(GameObject), false);
                    if (buttonTargetPrefab != null && !PrefabUtility.IsPartOfPrefabAsset(buttonTargetPrefab))
                        EditorGUILayout.HelpBox("Project内のPrefab Assetを指定してください。", MessageType.Warning);
                }
                buttonTargetChildPath = EditorGUILayout.TextField("Child Path", buttonTargetChildPath);
                EditorGUILayout.LabelField("例: Content または Content/Buttons", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("配置ボタンを押すと、指定Prefabを更新します。既存Prefabへの変更前に確認を表示し、同じButton IDの重複配置は拒否します。", MessageType.None);
            }
        }

        private void PlaceButtonPrefabIntoTarget(UIButtonEntry entry)
        {
            string buttonPath = GetButtonPrefabPath(entry);
            GameObject buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(buttonPath);
            if (buttonPrefab == null)
            {
                SetStatus("Button Prefabがありません。先に不足Prefabを作成してください。", MessageType.Error);
                return;
            }
            UIEventButton component = buttonPrefab.GetComponent<UIEventButton>();
            if (component == null || component.ButtonId != entry.id)
            {
                SetStatus("Button PrefabのUIEventButton IDがRegistryと一致しません。", MessageType.Error);
                return;
            }

            if (buttonPlacementMode == ButtonPlacementMode.CurrentPrefabStage)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null || stage.prefabContentsRoot == null)
                {
                    SetStatus("配置先Prefabが開かれていません。Prefab Modeで対象Prefabを開いてください。", MessageType.Warning);
                    return;
                }
                if (!EditorUtility.DisplayDialog("PrefabにButtonを配置", $"'{stage.assetPath}' の '{buttonTargetChildPath}' に '{entry.id}' を配置してPrefabを保存します。", "配置して保存", "キャンセル")) return;

                Transform target = FindChildPath(stage.prefabContentsRoot.transform, buttonTargetChildPath);
                if (target == null)
                {
                    SetStatus("配置先が見つかりません: " + buttonTargetChildPath, MessageType.Error);
                    return;
                }
                if (ContainsButtonId(target, entry.id))
                {
                    SetStatus("配置先に同じButton IDが既に存在します。重複配置を中止しました。", MessageType.Warning);
                    return;
                }
                if (string.Equals(stage.assetPath, buttonPath, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus("Button自身のPrefab内には配置できません。", MessageType.Error);
                    return;
                }

                try
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(buttonPrefab, stage.scene) as GameObject;
                    if (instance == null) throw new InvalidOperationException("Prefab instanceの生成に失敗しました。");
                    instance.transform.SetParent(target, false);
                    NormalizeRectTransform(instance);
                    EditorSceneManager.MarkSceneDirty(stage.scene);
                    PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out bool success);
                    if (!success) throw new InvalidOperationException("Prefabの保存に失敗しました。");
                    SetStatus($"'{entry.id}' をPrefabの '{buttonTargetChildPath}' に配置しました。", MessageType.Info);
                    EditorGUIUtility.PingObject(instance);
                }
                catch (Exception ex)
                {
                    SetStatus("Prefabへの配置に失敗しました: " + ex.Message, MessageType.Error);
                }
                return;
            }

            if (buttonTargetPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(buttonTargetPrefab))
            {
                SetStatus("配置先Prefab Assetを指定してください。", MessageType.Error);
                return;
            }
            string targetPath = AssetDatabase.GetAssetPath(buttonTargetPrefab);
            if (string.IsNullOrEmpty(targetPath) || string.Equals(targetPath, buttonPath, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("有効な配置先Prefabを指定してください。Button自身のPrefabは指定できません。", MessageType.Error);
                return;
            }
            if (!EditorUtility.DisplayDialog("PrefabにButtonを配置", $"'{targetPath}' の '{buttonTargetChildPath}' に '{entry.id}' を配置してPrefabを保存します。既存Prefabの内容が更新されます。", "配置して保存", "キャンセル")) return;

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(targetPath);
                if (contents == null) throw new InvalidOperationException("配置先Prefabを読み込めませんでした。");
                Transform target = FindChildPath(contents.transform, buttonTargetChildPath);
                if (target == null) throw new InvalidOperationException("配置先が見つかりません: " + buttonTargetChildPath);
                if (ContainsButtonId(target, entry.id)) throw new InvalidOperationException("配置先に同じButton IDが既に存在します。");
                GameObject instance = PrefabUtility.InstantiatePrefab(buttonPrefab, target) as GameObject;
                if (instance == null) throw new InvalidOperationException("Prefab instanceの生成に失敗しました。");
                NormalizeRectTransform(instance);
                PrefabUtility.SaveAsPrefabAsset(contents, targetPath, out bool success);
                if (!success) throw new InvalidOperationException("Prefabの保存に失敗しました。");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                SetStatus($"'{entry.id}' を '{targetPath}' の '{buttonTargetChildPath}' に配置しました。", MessageType.Info);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            }
            catch (Exception ex)
            {
                SetStatus("Prefabへの配置に失敗しました: " + ex.Message, MessageType.Error);
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Transform FindChildPath(Transform root, string childPath)
        {
            if (root == null) return null;
            if (string.IsNullOrWhiteSpace(childPath) || childPath == ".") return root;
            Transform current = root;
            string[] parts = childPath.Replace("\\", "/").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                current = current.Find(part);
                if (current == null) return null;
            }
            return current;
        }

        private static bool ContainsButtonId(Transform root, string id)
        {
            return root.GetComponentsInChildren<UIEventButton>(true).Any(x => x != null && x.ButtonId == id);
        }

        private static void NormalizeRectTransform(GameObject instance)
        {
            RectTransform rect = instance.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = Vector2.zero;
        }

        private void DrawButtonEntry(UIButtonEntry entry, int index)
        {
            string path = GetButtonPrefabPath(entry);
            PrefabHealth health = GetPrefabHealth(path, entry.prefabGuid, entry.prefabDependencyHash);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawAssetPreview(path);
                EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel, GUILayout.MinWidth(90));
                GUILayout.Label(entry.action.ToString(), EditorStyles.miniLabel, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                DrawHealthLabel(health);
                if (GUILayout.Button("Select", GUILayout.Width(48))) SelectAsset(path);
                using (new EditorGUI.DisabledScope(GetPrefabHealth(path, entry.prefabGuid, entry.prefabDependencyHash) == PrefabHealth.Missing || GetPrefabHealth(path, entry.prefabGuid, entry.prefabDependencyHash) == PrefabHealth.Invalid))
                    if (GUILayout.Button("配置", GUILayout.Width(44))) PlaceButtonPrefabIntoTarget(entry);
                if (GUILayout.Button("×", GUILayout.Width(24)) && EditorUtility.DisplayDialog("Button設定を削除", $"'{entry.id}' の設定だけを削除します。Prefabは削除しません。", "削除", "キャンセル"))
                {
                    buttonRegistry.EditorEntries.RemoveAt(index);
                    Save(buttonRegistry);
                    GUIUtility.ExitGUI();
                }
            }
            string key = "Uni7.Button.More." + entry.id;
            bool expanded = EditorPrefs.GetBool(key, false);
            bool next = EditorGUILayout.Foldout(expanded, "More Options", true);
            if (next != expanded) EditorPrefs.SetBool(key, next);
            if (next)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                entry.label = EditorGUILayout.TextField("Label", entry.label);
                entry.action = (UIButtonAction)EditorGUILayout.EnumPopup("Action", entry.action);
                DrawButtonActionSettings(entry);
                entry.pressScale = Mathf.Max(0f, EditorGUILayout.FloatField("Press Scale", entry.pressScale));
                entry.animationDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Animation Duration", entry.animationDuration));
                GameObject currentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                GameObject selectedPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab Asset", currentPrefab, typeof(GameObject), false);
                if (selectedPrefab != currentPrefab)
                {
                    string selectedPath = selectedPrefab != null ? AssetDatabase.GetAssetPath(selectedPrefab) : "";
                    UIEventButton selectedButton = selectedPrefab != null ? selectedPrefab.GetComponent<UIEventButton>() : null;
                    if (selectedButton == null || selectedButton.ButtonId != entry.id)
                        SetStatus("選択PrefabのUIEventButton IDが一致しません。", MessageType.Error);
                    else
                    {
                        entry.prefabPath = selectedPath;
                        entry.prefabGuid = AssetDatabase.AssetPathToGUID(selectedPath);
                        entry.prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(selectedPath).ToString();
                    }
                }
                EditorGUILayout.LabelField("Prefab Path", GetButtonPrefabPath(entry), EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck()) Save(buttonRegistry);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("不足Prefabを作成")) CreateMissingButtonPrefab(entry);
                    if (health == PrefabHealth.Changed || health == PrefabHealth.Unknown)
                        if (GUILayout.Button("現在のPrefabを基準にする")) RecordButtonPrefabBaseline(entry, path);
                }
                EditorGUI.indentLevel--;
            }
            }
        }

        private void DrawButtonActionSettings(UIButtonEntry entry)
        {
            if (entry.action == UIButtonAction.OpenWindow || entry.action == UIButtonAction.OpenModal || entry.action == UIButtonAction.CloseUI)
            {
                List<UIEntry> candidates = registry != null
                    ? registry.Entries.Where(x => x != null &&
                        (entry.action == UIButtonAction.CloseUI || x.type == (entry.action == UIButtonAction.OpenWindow ? UIType.Window : UIType.Modal))).ToList()
                    : new List<UIEntry>();
                DrawStringPopup("Destination UI", candidates.Select(x => x.id).ToArray(), ref entry.destinationUIId);
                if (entry.action == UIButtonAction.OpenWindow)
                    entry.closeCurrentBeforeOpen = EditorGUILayout.Toggle("Close current Window first", entry.closeCurrentBeforeOpen);
                if (candidates.Count == 0) EditorGUILayout.HelpBox("対象UIがありません。先にUI Tabで登録してください。", MessageType.Warning);
            }
            else if (entry.action == UIButtonAction.EmitEvent)
            {
                DrawEventSelection(entry);
            }
            else
            {
                EditorGUILayout.LabelField("このActionに追加パラメータはありません。", EditorStyles.miniLabel);
            }
        }

        private void DrawEventSelection(UIButtonEntry entry)
        {
            if (eventDatabase == null) eventDatabase = AssetDatabase.LoadAssetAtPath<EventDefinitionDatabase>(EventDatabasePath);
            if (eventDatabase == null || eventDatabase.categories == null || eventDatabase.categories.Count == 0)
            {
                EditorGUILayout.HelpBox("EventManagerの定義が見つかりません。先にTools > Uni7 > Events > GeneratorでEventを登録してください。", MessageType.Warning);
                return;
            }
            string[] categories = eventDatabase.categories.Where(x => x != null).Select(x => x.categoryName).ToArray();
            if (categories.Length == 0) return;
            int categoryIndex = Array.IndexOf(categories, entry.eventCategory);
            if (categoryIndex < 0) categoryIndex = 0;
            categoryIndex = EditorGUILayout.Popup("Event Category", categoryIndex, categories);
            entry.eventCategory = categories[categoryIndex];
            EventCategoryDefinition category = eventDatabase.categories.FirstOrDefault(x => x != null && x.categoryName == entry.eventCategory);
            if (category == null || category.events == null || category.events.Count == 0)
            {
                EditorGUILayout.HelpBox("このカテゴリにEventがありません。", MessageType.Info);
                return;
            }
            EventDefinition[] events = category.events.Where(x => x != null).ToArray();
            if (events.Length == 0)
            {
                EditorGUILayout.HelpBox("このカテゴリに有効なEventがありません。", MessageType.Info);
                return;
            }
            string[] names = events.Select(x => x.eventName).ToArray();
            int eventIndex = Array.IndexOf(names, entry.eventName);
            if (eventIndex < 0) eventIndex = 0;
            eventIndex = EditorGUILayout.Popup("Event", eventIndex, names);
            EventDefinition selected = events[eventIndex];
            entry.eventName = selected.eventName;
            entry.parameterType = selected.parameterType;
            EditorGUILayout.LabelField("Parameter", entry.parameterType.ToString());
            switch (entry.parameterType)
            {
                case EventParameterType.None: entry.parameterValue = ""; entry.objectParameter = null; break;
                case EventParameterType.GameObject: entry.objectParameter = (GameObject)EditorGUILayout.ObjectField("GameObject", entry.objectParameter, typeof(GameObject), false); break;
                default: entry.parameterValue = EditorGUILayout.TextField("Value", entry.parameterValue); break;
            }
        }

        private static void DrawStringPopup(string label, string[] options, ref string value)
        {
            if (options == null || options.Length == 0) { EditorGUILayout.LabelField(label, "(none)"); return; }
            int index = Array.IndexOf(options, value);
            if (index < 0) index = 0;
            index = EditorGUILayout.Popup(label, index, options);
            value = options[index];
        }

        private bool MatchesButton(UIButtonEntry entry)
        {
            if (buttonFilter == 1 && entry.action == UIButtonAction.EmitEvent) return false;
            if (buttonFilter == 2 && entry.action != UIButtonAction.EmitEvent) return false;
            return string.IsNullOrWhiteSpace(search) ||
                   (!string.IsNullOrEmpty(entry.id) && entry.id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(entry.label) && entry.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void CreateButtonEntry()
        {
            string id = newButtonId.Trim();
            if (!IsValidId(id)) { SetStatus("Button IDは英数字と_で入力してください。", MessageType.Error); return; }
            if (buttonRegistry.EditorEntries.Any(x => x != null && x.id == id)) { SetStatus("同じButton IDが存在します。", MessageType.Error); return; }
            UIButtonEntry entry = new UIButtonEntry { id = id, label = id, action = newButtonAction };
            if (entry.action == UIButtonAction.OpenWindow || entry.action == UIButtonAction.OpenModal || entry.action == UIButtonAction.CloseUI)
            {
                UIType required = entry.action == UIButtonAction.OpenWindow ? UIType.Window : UIType.Modal;
                UIEntry first = registry != null ? registry.Entries.FirstOrDefault(x => x != null &&
                    (entry.action == UIButtonAction.CloseUI || x.type == required)) : null;
                if (first != null) entry.destinationUIId = first.id;
            }
            buttonRegistry.EditorEntries.Add(entry);
            Save(buttonRegistry);
            if (!CreateButtonPrefab(entry, out string path))
            {
                buttonRegistry.EditorEntries.Remove(entry);
                Save(buttonRegistry);
                return;
            }
            Save(buttonRegistry);
            newButtonId = "";
            SetStatus($"Button '{id}' を登録しました。Prefabは新規作成です。", MessageType.Info);
            SelectAsset(path);
        }

        private void CreateMissingButtonPrefab(UIButtonEntry entry)
        {
            if (CreateButtonPrefab(entry, out string path))
            {
                Save(buttonRegistry);
                SetStatus("Button Prefabを作成しました: " + path, MessageType.Info);
            }
        }

        private bool CreateButtonPrefab(UIButtonEntry entry, out string path)
        {
            path = GetButtonPrefabPath(entry);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                UIEventButton existingButton = existing.GetComponent<UIEventButton>();
                if (existingButton == null || existingButton.ButtonId != entry.id)
                {
                    SetStatus("同名PrefabのButton IDが一致しません。既存Prefabは変更しません: " + path, MessageType.Error);
                    return false;
                }
                entry.prefabPath = path;
                if (string.IsNullOrEmpty(entry.prefabGuid)) RecordButtonPrefabBaseline(entry, path);
                return true;
            }
            if (File.Exists(path)) { SetStatus("同名ファイルがあるため作成を中止しました: " + path, MessageType.Error); return false; }
            if (!IsSafeAssetFolder(buttonPrefabRoot)) { SetStatus("Button Prefab FolderはAssets配下にしてください。", MessageType.Error); return false; }

            EnsureFolder(Path.GetDirectoryName(path).Replace("\\", "/"));
            GameObject template = CreateButtonTemplate(entry);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, path);
            DestroyImmediate(template);
            if (saved == null) { SetStatus("Button Prefab作成に失敗しました: " + path, MessageType.Error); return false; }
            entry.prefabPath = path;
            RecordButtonPrefabBaseline(entry, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        private static GameObject CreateButtonTemplate(UIButtonEntry entry)
        {
            GameObject root = new GameObject(entry.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.SetActive(false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 52f);
            root.GetComponent<Image>().color = new Color(0.22f, 0.34f, 0.52f, 1f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.transition = Selectable.Transition.ColorTint;
            UIEventButton eventButton = root.AddComponent<UIEventButton>();
            SerializedObject serialized = new SerializedObject(eventButton);
            serialized.FindProperty("buttonId").stringValue = entry.id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);

            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(root.transform, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = new Vector2(8f, 4f); labelRect.offsetMax = new Vector2(-8f, -4f);
            Text text = label.GetComponent<Text>();
            text.text = string.IsNullOrWhiteSpace(entry.label) ? entry.id : entry.label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = EditorStyles.label.font;
            return root;
        }

        // ---------------------------------------------------------------------
        // SETTINGS TAB
        // ---------------------------------------------------------------------
        private void DrawSettingsTab()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
            EditorGUILayout.LabelField("Workspace Settings", EditorStyles.boldLabel);
            prefabRoot = EditorGUILayout.TextField("UI Prefab Folder", prefabRoot);
            buttonPrefabRoot = EditorGUILayout.TextField("Button Prefab Folder", buttonPrefabRoot);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            DrawAssetRow("UI Registry", registry, RegistryPath);
            DrawAssetRow("Button Registry", buttonRegistry, ButtonRegistryPath);
            DrawAssetRow("Event Database", eventDatabase, EventDatabasePath);
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Scene上のUIManager/CanvasはUIRegistryをResourcesから読み込み、実行時に自動生成します。EventSystemはInput設定に依存するため既存Sceneのものを使用します。", MessageType.Info);
            EditorGUILayout.HelpBox("Prefabの編集は自動反映しません。Prefabの変更検知はGUIDとDependency Hashで行います。差分の内容を自動マージする機能ではありません。", MessageType.None);
            }
        }

        private void DrawAssetRow(string label, UnityEngine.Object asset, string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(115));
                EditorGUILayout.ObjectField(asset, typeof(UnityEngine.Object), false);
                if (GUILayout.Button("Select", GUILayout.Width(52)))
                {
                    UnityEngine.Object loaded = AssetDatabase.LoadMainAssetAtPath(path);
                    if (loaded != null) Selection.activeObject = loaded;
                    else SetStatus("Assetがありません: " + path, MessageType.Warning);
                }
            }
        }

        // ---------------------------------------------------------------------
        // ASSET / SAFETY HELPERS
        // ---------------------------------------------------------------------
        private enum PrefabHealth { Missing, Healthy, Changed, Unknown, Invalid }

        private static PrefabHealth GetPrefabHealth(string path, string expectedGuid, string expectedHash)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return File.Exists(path) ? PrefabHealth.Invalid : PrefabHealth.Missing;
            string guid = AssetDatabase.AssetPathToGUID(path);
            string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            if (string.IsNullOrEmpty(expectedGuid) || string.IsNullOrEmpty(expectedHash)) return PrefabHealth.Unknown;
            return guid == expectedGuid && hash == expectedHash ? PrefabHealth.Healthy : PrefabHealth.Changed;
        }

        private static void DrawAssetPreview(string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                GUILayout.Box(GUIContent.none, GUILayout.Width(48), GUILayout.Height(42));
                return;
            }
            Texture2D preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null) preview = AssetPreview.GetMiniThumbnail(asset);
            GUILayout.Label(preview, GUILayout.Width(48), GUILayout.Height(42));
        }

        private static void DrawHealthLabel(PrefabHealth health)
        {
            string text = health switch
            {
                PrefabHealth.Healthy => "Prefab OK",
                PrefabHealth.Changed => "変更あり",
                PrefabHealth.Unknown => "未確認",
                PrefabHealth.Invalid => "不正ファイル",
                _ => "Prefabなし"
            };
            Color old = GUI.contentColor;
            if (health == PrefabHealth.Changed || health == PrefabHealth.Invalid) GUI.contentColor = new Color(1f, 0.55f, 0.35f);
            else if (health == PrefabHealth.Healthy) GUI.contentColor = new Color(0.45f, 0.85f, 0.55f);
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(72));
            GUI.contentColor = old;
        }

        private void RecordPrefabBaseline(UIEntry entry, string path)
        {
            entry.prefabPath = path;
            entry.prefabGuid = AssetDatabase.AssetPathToGUID(path);
            entry.prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            Save(registry);
            SetStatus("Prefabの現在状態を基準として記録しました。", MessageType.Info);
        }

        private void RecordButtonPrefabBaseline(UIButtonEntry entry, string path)
        {
            entry.prefabPath = path;
            entry.prefabGuid = AssetDatabase.AssetPathToGUID(path);
            entry.prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            Save(buttonRegistry);
            SetStatus("Button Prefabの現在状態を基準として記録しました。", MessageType.Info);
        }

        private bool EnsureAddressable(UIEntry entry, bool showMessage)
        {
            if (string.IsNullOrWhiteSpace(entry.address))
            {
                if (showMessage) SetStatus("Addressables Addressが空です。", MessageType.Error);
                return false;
            }
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                if (showMessage) SetStatus("Addressables Settingsがありません。Window > Asset Management > Addressables > Groupsから初期化してください。", MessageType.Error);
                return false;
            }
            string path = GetPrefabPath(entry);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { if (showMessage) SetStatus("Prefab GUIDを取得できません: " + path, MessageType.Error); return false; }

            AddressableAssetEntry byGuid = settings.FindAssetEntry(guid);
            if (byGuid != null)
            {
                if (byGuid.address == entry.address) return true;
                if (showMessage) SetStatus($"このPrefabはAddress '{byGuid.address}' で登録済みです。既存Addressは変更しません。", MessageType.Warning);
                return false;
            }
            AddressableAssetEntry conflict = settings.groups.Where(g => g != null).SelectMany(g => g.entries)
                .FirstOrDefault(x => x != null && x.address == entry.address);
            if (conflict != null) { if (showMessage) SetStatus($"Address '{entry.address}' は別アセットで使用中です。", MessageType.Error); return false; }
            if (settings.DefaultGroup == null) { if (showMessage) SetStatus("Addressables Default Groupがありません。", MessageType.Error); return false; }
            AddressableAssetEntry created = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            created.address = entry.address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryCreated, created, true);
            return true;
        }

        private void UpdateAddressableAddress(UIEntry entry)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { SetStatus("Addressables Settingsがありません。", MessageType.Error); return; }
            string path = GetPrefabPath(entry);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { SetStatus("Prefab GUIDを取得できません: " + path, MessageType.Error); return; }
            AddressableAssetEntry current = settings.FindAssetEntry(guid);
            if (current == null)
            {
                if (EnsureAddressable(entry, true)) SetStatus("Addressablesに登録しました。", MessageType.Info);
                return;
            }
            AddressableAssetEntry conflict = settings.groups.Where(group => group != null).SelectMany(group => group.entries)
                .FirstOrDefault(candidate => candidate != null && candidate.guid != guid && candidate.address == entry.address);
            if (conflict != null) { SetStatus($"Address '{entry.address}' は別アセットで使用中です。", MessageType.Error); return; }
            if (current.address == entry.address) { SetStatus("Addressables Addressは既に一致しています。", MessageType.Info); return; }
            if (!EditorUtility.DisplayDialog("Addressables Addressの変更",
                $"このPrefabのAddressを変更しますか？\n\n{current.address}  →  {entry.address}\n\nPrefab自体は変更しません。",
                "Addressを変更", "キャンセル")) return;
            current.address = entry.address;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            SetStatus("Addressables Addressを更新しました。", MessageType.Info);
        }

        private string GetPrefabPath(UIEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.prefabPath)) return entry.prefabPath;
            string folder = entry.type == UIType.Window ? "Windows" : "Modals";
            return $"{prefabRoot.TrimEnd('/')}/{folder}/{SanitizeFileName(entry.id)}.prefab";
        }

        private string GetButtonPrefabPath(UIButtonEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.prefabPath)) return entry.prefabPath;
            return $"{buttonPrefabRoot.TrimEnd('/')}/{SanitizeFileName(entry.id)}.prefab";
        }

        private void LoadAssets()
        {
            registry = AssetDatabase.LoadAssetAtPath<UIRegistry>(RegistryPath);
            buttonRegistry = AssetDatabase.LoadAssetAtPath<UIButtonRegistry>(ButtonRegistryPath);
            eventDatabase = AssetDatabase.LoadAssetAtPath<EventDefinitionDatabase>(EventDatabasePath);
            if (registry == null) SetStatus("UIRegistryがありません。UI Tabから作成してください。", MessageType.Warning);
            else SetStatus("Registryを読み込みました。", MessageType.Info);
            Repaint();
        }

        private void CreateUIRegistry()
        {
            EnsureFolder("Assets/Resources/Uni7");
            if (AssetDatabase.LoadAssetAtPath<UIRegistry>(RegistryPath) != null) { LoadAssets(); return; }
            registry = CreateInstance<UIRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            SetStatus("UIRegistryを作成しました。", MessageType.Info);
        }

        private void CreateButtonRegistry()
        {
            EnsureFolder("Assets/Resources/Uni7");
            if (AssetDatabase.LoadAssetAtPath<UIButtonRegistry>(ButtonRegistryPath) != null) { LoadAssets(); return; }
            buttonRegistry = CreateInstance<UIButtonRegistry>();
            AssetDatabase.CreateAsset(buttonRegistry, ButtonRegistryPath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            SetStatus("UIButtonRegistryを作成しました。", MessageType.Info);
        }

        private static void Save(UnityEngine.Object asset)
        {
            if (asset == null) return;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static bool IsSafeAssetFolder(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains("\\");
        }

        private static bool IsValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
            return value.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folder = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(parent, folder);
        }

        private static void SelectAsset(string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null) Selection.activeObject = asset;
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }

        private void ValidateUIEntries()
        {
            List<string> errors = new List<string>();
            if (registry == null) errors.Add("UIRegistryがありません。");
            else
            {
                var duplicates = registry.Entries.Where(x => x != null && !string.IsNullOrEmpty(x.id))
                    .GroupBy(x => x.id).Where(group => group.Count() > 1).Select(group => group.Key);
                foreach (string id in duplicates) errors.Add($"UI ID重複: {id}");

                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                foreach (UIEntry entry in registry.Entries.Where(x => x != null))
                {
                    if (!IsValidId(entry.id)) errors.Add($"IDが不正: {entry.id}");
                    string path = GetPrefabPath(entry);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) errors.Add($"Prefabなし: {entry.id} ({path})");
                    else
                    {
                        Type expected = entry.type == UIType.Window ? typeof(UIWindow) : typeof(UIModal);
                        if (prefab.GetComponent(expected) == null) errors.Add($"Prefabに{expected.Name}がありません: {entry.id}");
                        string guid = AssetDatabase.AssetPathToGUID(path);
                        AddressableAssetEntry addressable = settings != null ? settings.FindAssetEntry(guid) : null;
                        if (addressable == null) errors.Add($"Addressables未登録: {entry.id}");
                        else if (addressable.address != entry.address) errors.Add($"Address不一致: {entry.id} (Registry={entry.address}, Asset={addressable.address})");
                    }
                }
            }
            ShowValidationResult("UI Validation", errors);
        }

        private void ValidateButtonEntries()
        {
            List<string> errors = new List<string>();
            if (buttonRegistry == null) errors.Add("UIButtonRegistryがありません。");
            else
            {
                var duplicates = buttonRegistry.Entries.Where(x => x != null && !string.IsNullOrEmpty(x.id))
                    .GroupBy(x => x.id).Where(group => group.Count() > 1).Select(group => group.Key);
                foreach (string id in duplicates) errors.Add($"Button ID重複: {id}");

                foreach (UIButtonEntry entry in buttonRegistry.Entries.Where(x => x != null))
                {
                    if (!IsValidId(entry.id)) errors.Add($"Button IDが不正: {entry.id}");
                    string path = GetButtonPrefabPath(entry);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) errors.Add($"Button Prefabなし: {entry.id} ({path})");
                    else
                    {
                        UIEventButton component = prefab.GetComponent<UIEventButton>();
                        if (component == null || component.ButtonId != entry.id) errors.Add($"Button PrefabのID不一致: {entry.id}");
                    }

                    if (entry.action == UIButtonAction.OpenWindow || entry.action == UIButtonAction.OpenModal || entry.action == UIButtonAction.CloseUI)
                    {
                        if (registry == null || !registry.TryGet(entry.destinationUIId, out UIEntry destination))
                            errors.Add($"遷移先UIが見つかりません: {entry.id} -> {entry.destinationUIId}");
                        else if (entry.action == UIButtonAction.OpenWindow && destination.type != UIType.Window)
                            errors.Add($"Window以外が指定されています: {entry.id}");
                        else if (entry.action == UIButtonAction.OpenModal && destination.type != UIType.Modal)
                            errors.Add($"Modal以外が指定されています: {entry.id}");
                    }
                    else if (entry.action == UIButtonAction.EmitEvent)
                    {
                        if (!File.Exists("Assets/Scripts/GenerateEvents/EventManager.cs"))
                            errors.Add("EventManagerコードが未生成です。Event GeneratorでSave + Generateしてください。");
                        EventCategoryDefinition category = eventDatabase != null
                            ? eventDatabase.categories.FirstOrDefault(x => x != null && x.categoryName == entry.eventCategory)
                            : null;
                        EventDefinition definition = category != null && category.events != null
                            ? category.events.FirstOrDefault(x => x != null && x.eventName == entry.eventName)
                            : null;
                        if (definition == null) errors.Add($"Eventが見つかりません: {entry.id} -> {entry.eventCategory}.{entry.eventName}");
                        else if (definition.parameterType != entry.parameterType)
                            errors.Add($"Event引数の型が変更されています: {entry.id} -> {entry.eventCategory}.{entry.eventName}");
                    }
                }
            }
            ShowValidationResult("Button Validation", errors);
        }

        private void ShowValidationResult(string title, List<string> errors)
        {
            if (errors.Count == 0)
            {
                SetStatus(title + ": 問題は見つかりませんでした。", MessageType.Info);
                EditorUtility.DisplayDialog(title, "問題は見つかりませんでした。", "OK");
                return;
            }
            string message = string.Join("\n", errors.Take(25));
            if (errors.Count > 25) message += $"\n...ほか {errors.Count - 25} 件";
            SetStatus(title + $": {errors.Count} 件の問題があります。", MessageType.Warning);
            EditorUtility.DisplayDialog(title, $"{errors.Count} 件の問題が見つかりました。\n\n{message}", "OK");
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(status)) return;
            EditorGUILayout.HelpBox(status, statusType);
        }
    }
}
#endif
