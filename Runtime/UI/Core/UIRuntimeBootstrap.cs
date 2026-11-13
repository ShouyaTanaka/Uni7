using UnityEngine;
using UnityEngine.UI;

namespace Uni7.UI
{
    /// <summary>
    /// UIRegistryがResources/Uni7/UIRegistryに存在する場合、Sceneに何も置かずにUIルートを生成します。
    /// EventSystemはプロジェクトのInput設定に依存するため自動生成しません。
    /// </summary>
    public static class UIRuntimeBootstrap
    {
        private const string RegistryResourcePath = "Uni7/UIRegistry";
        private const string RootName = "[Uni7] UI Runtime";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntimeUI()
        {
            if (UIManager.Instance != null) return;

            UIRegistry registry = Resources.Load<UIRegistry>(RegistryResourcePath);
            if (registry == null)
            {
                Debug.LogWarning($"[Uni7 UI] UIRegistryがResources/{RegistryResourcePath}.assetに見つかりません。UI GeneratorからRegistryを作成してください。");
                return;
            }

            GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(root);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform windows = CreateLayer("Windows", root.transform);
            Transform modals = CreateLayer("Modals", root.transform);

            UIManager manager = root.AddComponent<UIManager>();
            manager.ConfigureRuntime(registry, windows, modals);
        }

        private static Transform CreateLayer(string name, Transform parent)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform));
            layer.transform.SetParent(parent, false);
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return layer.transform;
        }
    }
}
