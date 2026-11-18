#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Uni7.Editor.Setup
{
    /// <summary>
    /// Uni7の初期セットアップ画面。
    /// </summary>
    public sealed class Uni7SetupWindow : EditorWindow
    {
        private string statusMessage = "セットアップを開始できます。";
        private MessageType statusType = MessageType.Info;
        private bool isInstalling;
        private string additionalDefineSymbols = string.Empty;
        private BuildTargetGroup loadedSymbolTarget;

        private void OnEnable()
        {
            RefreshSymbolsForCurrentTarget();
        }

        private void RefreshSymbolsForCurrentTarget()
        {
            loadedSymbolTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
            additionalDefineSymbols = Uni7SetupRunner.GetAdditionalDefineSymbols();
        }

        [MenuItem("Tools/Uni7/Setup Manager")]
        public static void Open()
        {
            GetWindow<Uni7SetupWindow>("Uni7 Setup");
        }

        private void OnGUI()
        {
            if (loadedSymbolTarget != EditorUserBuildSettings.selectedBuildTargetGroup)
                RefreshSymbolsForCurrentTarget();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Uni7 Setup Manager", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Uni7を利用するためのUnityプロジェクト環境をセットアップします。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(12);

            EditorGUILayout.HelpBox(
                "Uni7本体はUPMパッケージ内に配置されます。" +
                "この処理ではAssets/Uni7を作成せず、プロジェクト側の開発用フォルダのみを生成します。",
                MessageType.Info);

            EditorGUILayout.LabelField("Project Folders", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Scripts / Resources / Images / Sounds / BGM / SE / Materials / Prefabs");

            using (new EditorGUI.DisabledScope(isInstalling))
            {
                if (GUILayout.Button("Create / Repair Project Folders", GUILayout.Height(30)))
                {
                    int created = Uni7SetupRunner.CreateProjectFolders();
                    SetStatus(
                        created == 0
                            ? "必要なフォルダはすべて存在しています。"
                            : created + " 個のフォルダを作成しました。",
                        MessageType.Info);
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("External Packages", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Core Event Manager: UniTask / UniRx");

                if (GUILayout.Button("Check & Install Dependencies", GUILayout.Height(30)))
                {
                    isInstalling = true;
                    SetStatus("Packageの状態を確認しています...", MessageType.Info);

                    Uni7SetupRunner.InstallDependencies(
                        message =>
                        {
                            statusMessage = message;
                            Repaint();
                        },
                        (success, message) =>
                        {
                            isInstalling = false;
                            SetStatus(message, success ? MessageType.Info : MessageType.Error);
                            Repaint();
                        });
                }

                EditorGUILayout.Space(12);
                DrawModuleSection();

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("UI Module - Addressables", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "UI Moduleを有効化した後、Addressables Settingsを確認します。未作成の場合だけUnity標準のSettingsを生成し、既存Settingsは上書きしません。",
                    MessageType.Info);

                using (new EditorGUI.DisabledScope(!Uni7SetupRunner.IsUIModuleEnabled || isInstalling || EditorApplication.isCompiling || EditorApplication.isUpdating))
                {
                    if (GUILayout.Button("Check / Create Addressables Settings", GUILayout.Height(30)))
                    {
                        bool success = Uni7SetupRunner.EnsureAddressablesSettings(out string message);
                        SetStatus(message, success ? MessageType.Info : MessageType.Warning);
                    }
                }

                EditorGUILayout.Space(8);
                if (GUILayout.Button("Run Full Setup", GUILayout.Height(36)))
                {
                    int created = Uni7SetupRunner.CreateProjectFolders();
                    isInstalling = true;
                    SetStatus(
                        "フォルダを確認しました（新規作成: " + created + "）。Core Event Manager用Packageを確認しています...",
                        MessageType.Info);

                    Uni7SetupRunner.InstallDependencies(
                        message =>
                        {
                            statusMessage = message;
                            Repaint();
                        },
                        (success, message) =>
                        {
                            isInstalling = false;
                            SetStatus(message, success ? MessageType.Info : MessageType.Error);
                            Repaint();
                        });
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(statusMessage, statusType);

            if (isInstalling)
                EditorGUILayout.LabelField("Package Managerの処理中です...", EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                "既存フォルダや既存ファイルは削除・上書きしません。",
                EditorStyles.miniLabel);
        }

        private void DrawModuleSection()
        {
            EditorGUILayout.LabelField("Optional Modules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "UIは任意モジュールです。有効化すると必要Packageを確認・導入した後、選択中のBuild TargetにUNI7_UIを追加します。無効化してもPackageや既存アセットは削除しません。",
                MessageType.Info);

            bool uiEnabled = Uni7SetupRunner.IsUIModuleEnabled;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("UI Module", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(uiEnabled ? "Enabled" : "Disabled", GUILayout.Width(65));
            }

            using (new EditorGUI.DisabledScope(isInstalling || EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                if (!uiEnabled)
                {
                    if (GUILayout.Button("Enable UI Module (install requirements)", GUILayout.Height(30)))
                    {
                        isInstalling = true;
                        SetStatus("UI Moduleの依存Packageを確認しています...", MessageType.Info);
                        Uni7SetupRunner.EnableUIModule(
                            message => { statusMessage = message; Repaint(); },
                            (success, message) =>
                            {
                                isInstalling = false;
                                SetStatus(message, success ? MessageType.Info : MessageType.Error);
                                Repaint();
                            });
                    }
                }
                else if (GUILayout.Button("Disable UI Module", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Disable UI Module",
                        "UNI7_UIを選択中のBuild Targetから削除します。UI関連Packageや既存ファイルは削除しません。Uni7.UI型を直接参照しているユーザースクリプトは、無効化後にコンパイルエラーになるため別途条件付きコンパイルしてください。続行しますか？",
                        "Disable", "Cancel"))
                    {
                        if (Uni7SetupRunner.DisableUIModule(out string message))
                            SetStatus(message, MessageType.Info);
                        else
                            SetStatus(message, MessageType.Error);
                    }
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Additional Scripting Define Symbols", EditorStyles.boldLabel);
            additionalDefineSymbols = EditorGUILayout.TextField(
                new GUIContent("Symbols", "セミコロン、カンマ、空白区切りで入力できます。UNI7_UIはモジュールスイッチで管理します。"),
                additionalDefineSymbols);
            EditorGUILayout.LabelField("Target: " + EditorUserBuildSettings.selectedBuildTargetGroup + " (selected target only)", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(isInstalling || EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply Additional Symbols"))
                    {
                        if (Uni7SetupRunner.ApplyAdditionalDefineSymbols(additionalDefineSymbols, out string message))
                            SetStatus(message, MessageType.Info);
                        else
                            SetStatus(message, MessageType.Error);
                    }

                    if (GUILayout.Button("Reload", GUILayout.Width(70)))
                        RefreshSymbolsForCurrentTarget();
                }
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }
    }
}
#endif
