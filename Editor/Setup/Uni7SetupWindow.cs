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

        [MenuItem("Tools/Uni7/Setup Manager")]
        public static void Open()
        {
            GetWindow<Uni7SetupWindow>("Uni7 Setup");
        }

        private void OnGUI()
        {
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
                EditorGUILayout.LabelField("UniTask / UniRx (Git URL)/ Addressables ");

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

                EditorGUILayout.Space(8);
                if (GUILayout.Button("Run Full Setup", GUILayout.Height(36)))
                {
                    int created = Uni7SetupRunner.CreateProjectFolders();
                    isInstalling = true;
                    SetStatus(
                        "フォルダを確認しました（新規作成: " + created + "）。Packageを確認しています...",
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

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }
    }
}
#endif
