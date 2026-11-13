#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Uni7.Editor.Setup
{
    /// <summary>
    /// Uni7を導入したUnityプロジェクトに、推奨フォルダと外部Packageをセットアップします。
    /// 既存フォルダは削除・上書きしません。
    /// </summary>
    internal static class Uni7SetupRunner
    {
        private static readonly string[] RequiredFolders =
        {
            "Assets/Scripts",
            "Assets/Resources",
            "Assets/Resources/Images",
            "Assets/Resources/Sounds",
            "Assets/Resources/Sounds/BGM",
            "Assets/Resources/Sounds/SE",
            "Assets/Resources/Materials",
            "Assets/Prefabs",
        };

        private static ListRequest listRequest;
        private static AddRequest addRequest;
        private static readonly Queue<PackageDefinition> installQueue =
            new Queue<PackageDefinition>();

        private static Action<string> onProgress;
        private static Action<bool, string> onCompleted;
        private static bool isRunning;

        private readonly struct PackageDefinition
        {
            public readonly string Name;
            public readonly string GitUrl;

            public PackageDefinition(string name, string gitUrl)
            {
                Name = name;
                GitUrl = gitUrl;
            }
        }

        public static bool IsRunning => isRunning;

        /// <summary>
        /// AddressablesのSettingsを確認し、存在しない場合だけデフォルト設定を生成します。
        /// Addressablesへのコンパイル時参照を避けるためReflectionで呼び出します。
        /// </summary>
        public static bool EnsureAddressablesSettings(out string message)
        {
            message = string.Empty;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                message = "UnityがコンパイルまたはPackage更新中です。完了してから再試行してください。";
                return false;
            }

            try
            {
                Type defaultObjectType = FindType(
                    "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");

                if (defaultObjectType == null)
                {
                    message = "Addressables Editor APIが見つかりません。Addressablesの導入と再コンパイルが完了しているか確認してください。";
                    return false;
                }

                PropertyInfo existsProperty = defaultObjectType.GetProperty(
                    "SettingsExists", BindingFlags.Public | BindingFlags.Static);
                PropertyInfo settingsProperty = defaultObjectType.GetProperty(
                    "Settings", BindingFlags.Public | BindingFlags.Static);
                bool settingsExists = existsProperty != null &&
                    existsProperty.PropertyType == typeof(bool)
                    ? (bool)existsProperty.GetValue(null)
                    : settingsProperty != null && settingsProperty.GetValue(null) != null;

                MethodInfo getSettings = defaultObjectType.GetMethod(
                    "GetSettings", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool) }, null);

                if (getSettings == null)
                {
                    message = "AddressablesのGetSettings(bool) APIが見つかりません。インストールされているバージョンとの互換性を確認してください。";
                    return false;
                }

                // 既存設定がある場合は取得のみ。新規生成を要求しない。
                object settings = getSettings.Invoke(null, new object[] { !settingsExists });
                if (settings == null)
                {
                    message = "Addressables Settingsを取得・生成できませんでした。";
                    return false;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                PropertyInfo defaultGroupProperty = settings.GetType().GetProperty(
                    "DefaultGroup", BindingFlags.Public | BindingFlags.Instance);
                object defaultGroup = defaultGroupProperty?.GetValue(settings);

                if (settingsExists)
                {
                    message = defaultGroup != null
                        ? "既存のAddressables Settingsを確認しました。既存設定は変更していません。"
                        : "Addressables Settingsは存在しますがDefault Groupがありません。既存設定を保護するため、自動変更は行っていません。";
                    return defaultGroup != null;
                }

                message = defaultGroup != null
                    ? "Addressables SettingsとDefault Groupを新規作成しました。"
                    : "Addressables Settingsを新規作成しましたが、Default Groupを確認できませんでした。Addressables Groupsを確認してください。";
                return defaultGroup != null;
            }
            catch (TargetInvocationException exception)
            {
                Exception cause = exception.InnerException ?? exception;
                message = "Addressables初期化中にエラーが発生しました。\n" + cause.Message;
                return false;
            }
            catch (Exception exception)
            {
                message = "Addressables初期化中にエラーが発生しました。\n" + exception.Message;
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        public static int CreateProjectFolders()
        {
            int createdCount = 0;

            foreach (string folder in RequiredFolders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                    continue;

                string absolutePath = Path.GetFullPath(folder);
                Directory.CreateDirectory(absolutePath);
                createdCount++;
            }

            AssetDatabase.Refresh();
            return createdCount;
        }

        public static void InstallDependencies(
            Action<string> progress,
            Action<bool, string> completed)
        {
            if (isRunning)
            {
                completed?.Invoke(false, "Packageのセットアップは既に実行中です。");
                return;
            }

            isRunning = true;
            onProgress = progress;
            onCompleted = completed;

            installQueue.Clear();
            installQueue.Enqueue(new PackageDefinition(
                PackageInfoConst.UniTaskName, PackageInfoConst.UniTaskGit));
            installQueue.Enqueue(new PackageDefinition(
                PackageInfoConst.UniRxName, PackageInfoConst.UniRxGit));
            installQueue.Enqueue(new PackageDefinition(
                PackageInfoConst.AddressablesName, PackageInfoConst.AddressablesGit));

            onProgress?.Invoke("インストール済みPackageを確認しています...");
            listRequest = Client.List(true, true);
            EditorApplication.update += UpdatePackageInstallation;
        }

        private static void UpdatePackageInstallation()
        {
            if (listRequest != null)
            {
                if (!listRequest.IsCompleted)
                    return;

                if (listRequest.Status == StatusCode.Failure)
                {
                    Finish(false, "Package一覧の取得に失敗しました。\n" + listRequest.Error.message);
                    return;
                }

                var installedNames = new HashSet<string>();
                foreach (var package in listRequest.Result)
                    installedNames.Add(package.name);

                var pending = new Queue<PackageDefinition>();
                while (installQueue.Count > 0)
                {
                    PackageDefinition package = installQueue.Dequeue();
                    if (!installedNames.Contains(package.Name))
                        pending.Enqueue(package);
                }

                installQueue.Clear();
                while (pending.Count > 0)
                    installQueue.Enqueue(pending.Dequeue());

                listRequest = null;
            }

            if (addRequest != null)
            {
                if (!addRequest.IsCompleted)
                    return;

                if (addRequest.Status == StatusCode.Failure)
                {
                    Finish(false, "Packageの追加に失敗しました。\n" + addRequest.Error.message);
                    return;
                }

                onProgress?.Invoke(addRequest.Result.displayName + " の導入が完了しました。");
                addRequest = null;
            }

            if (installQueue.Count == 0)
            {
                Finish(true, "外部Packageの確認・導入が完了しました。");
                return;
            }

            PackageDefinition next = installQueue.Dequeue();
            onProgress?.Invoke(next.Name + " を導入しています...");
            addRequest = Client.Add(next.GitUrl);
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= UpdatePackageInstallation;
            listRequest = null;
            addRequest = null;
            installQueue.Clear();
            isRunning = false;

            Action<bool, string> callback = onCompleted;
            onProgress = null;
            onCompleted = null;

            callback?.Invoke(success, message);
        }
    }
}
#endif
