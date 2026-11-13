using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Uni7.Events;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Uni7.UI
{
    // Kept for source compatibility with early Uni7 previews.
    public enum UIButtonTransition { OpenWindow, OpenModal, CloseCurrent }

    [RequireComponent(typeof(Button))]
    public sealed class UIEventButton : MonoBehaviour
    {
        private const string ButtonRegistryResourcePath = "Uni7/UIButtonRegistry";

        [SerializeField] private string buttonId;
        [SerializeField, FormerlySerializedAs("transition")] private UIButtonAction action;
        [SerializeField] private string destinationUIId;
        [SerializeField] private bool closeCurrentBeforeOpen;
        [SerializeField] private string eventCategory;
        [SerializeField] private string eventName;
        [SerializeField] private EventParameterType parameterType;
        [SerializeField] private string parameterValue;
        [SerializeField] private GameObject objectParameter;
        [SerializeField, Min(0f)] private float pressScale = 0.94f;
        [SerializeField, Min(0f)] private float animationDuration = 0.08f;

        private Button button;
        private Vector3 initialScale;
        private bool isProcessing;
        private UIButtonEntry entry;

        public string ButtonId => buttonId;
        public string DestinationUIId => entry != null ? entry.destinationUIId : destinationUIId;
        public UIButtonAction Action => entry != null ? entry.action : action;

        private void Awake()
        {
            button = GetComponent<Button>();
            initialScale = transform.localScale;
            if (!string.IsNullOrEmpty(buttonId))
            {
                UIButtonRegistry registry = Resources.Load<UIButtonRegistry>(ButtonRegistryResourcePath);
                if (registry == null || !registry.TryGet(buttonId, out entry))
                    Debug.LogError($"[Uni7 UI] Button ID '{buttonId}' がUIButtonRegistryに見つかりません。", this);
                else
                {
                    Text label = GetComponentInChildren<Text>(true);
                    if (label != null && !string.IsNullOrEmpty(entry.label)) label.text = entry.label;
                }
            }
            button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (isProcessing || button == null || !button.interactable) return;
            isProcessing = true;
            button.interactable = false;
            RunClickAsync().Forget();
        }

        private async UniTaskVoid RunClickAsync()
        {
            try
            {
                await AnimatePressAsync();
                UIButtonAction currentAction = entry != null ? entry.action : action;
                string targetId = entry != null ? entry.destinationUIId : destinationUIId;
                bool closeBeforeOpen = entry != null ? entry.closeCurrentBeforeOpen : closeCurrentBeforeOpen;
                switch (currentAction)
                {
                    case UIButtonAction.OpenWindow:
                        if (closeBeforeOpen && UIManager.Instance != null)
                            await UIManager.Instance.CloseAllWindowsAsync(this.GetCancellationTokenOnDestroy());
                        UIEventHub.PublishOpenWindow(targetId);
                        break;
                    case UIButtonAction.OpenModal:
                        UIEventHub.PublishOpenModal(targetId);
                        break;
                    case UIButtonAction.CloseCurrent:
                        UIBase owner = GetComponentInParent<UIBase>();
                        if (owner != null && !string.IsNullOrEmpty(owner.UIId)) UIEventHub.PublishCloseUI(owner.UIId);
                        break;
                    case UIButtonAction.CloseTopModal:
                        UIEventHub.PublishCloseTopModal();
                        break;
                    case UIButtonAction.CloseAll:
                        UIEventHub.PublishCloseAll();
                        break;
                    case UIButtonAction.CloseUI:
                        UIEventHub.PublishCloseUI(targetId);
                        break;
                    case UIButtonAction.EmitEvent:
                        EmitCustomEvent();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // The button was destroyed while its animation or transition was running.
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
            finally
            {
                if (this != null && button != null)
                {
                    button.interactable = true;
                    isProcessing = false;
                }
            }
        }

        private void EmitCustomEvent()
        {
            string categoryName = entry != null ? entry.eventCategory : eventCategory;
            string customEventName = entry != null ? entry.eventName : eventName;
            EventParameterType type = entry != null ? entry.parameterType : parameterType;
            string valueText = entry != null ? entry.parameterValue : parameterValue;
            GameObject goValue = entry != null ? entry.objectParameter : objectParameter;

            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(customEventName))
                throw new InvalidOperationException("ButtonのEventカテゴリまたはEvent名が未設定です。");

            // Generated EventManager is in the project's generated assembly. Reflection avoids a circular asmdef reference.
            Type managerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("EventManager", false))
                .FirstOrDefault(typeInfo => typeInfo != null);
            if (managerType == null)
                throw new InvalidOperationException("EventManagerが見つかりません。Event Generatorでコードを生成してください。");

            PropertyInfo categoryProperty = managerType.GetProperty(categoryName, BindingFlags.Public | BindingFlags.Static);
            object category = categoryProperty?.GetValue(null);
            if (category == null) throw new InvalidOperationException($"Eventカテゴリ '{categoryName}' が見つかりません。");

            string methodName = "Emit" + customEventName;
            int parameterCount = type == EventParameterType.None ? 0 : 1;
            MethodInfo method = category.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);
            if (method == null) throw new InvalidOperationException($"Event '{categoryName}.{customEventName}' が見つかりません。Event Generatorで再生成してください。");
            if (parameterCount == 0) { method.Invoke(category, null); return; }
            method.Invoke(category, new[] { ParseParameter(type, valueText, goValue) });
        }

        private static object ParseParameter(EventParameterType type, string value, GameObject objectValue)
        {
            switch (type)
            {
                case EventParameterType.String: return value ?? string.Empty;
                case EventParameterType.Int: return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                case EventParameterType.Float: return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                case EventParameterType.Bool: return bool.Parse(value);
                case EventParameterType.Vector2:
                {
                    string[] p = (value ?? "").Split(',');
                    if (p.Length != 2) throw new FormatException("Vector2は 'x,y' 形式で入力してください。");
                    return new Vector2(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                case EventParameterType.Vector3:
                {
                    string[] p = (value ?? "").Split(',');
                    if (p.Length != 3) throw new FormatException("Vector3は 'x,y,z' 形式で入力してください。");
                    return new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
                }
                case EventParameterType.GameObject: return objectValue;
                default: return null;
            }
        }

        private async UniTask AnimatePressAsync()
        {
            float scale = entry != null ? entry.pressScale : pressScale;
            float duration = entry != null ? entry.animationDuration : animationDuration;
            if (duration <= 0f) return;
            var token = this.GetCancellationTokenOnDestroy();
            Vector3 pressed = initialScale * scale;
            await AnimateScaleAsync(initialScale, pressed, duration, token);
            await AnimateScaleAsync(pressed, initialScale, duration, token);
        }

        private async UniTask AnimateScaleAsync(Vector3 from, Vector3 to, float duration, System.Threading.CancellationToken token)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);
                transform.localScale = Vector3.LerpUnclamped(from, to, t);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            transform.localScale = to;
        }
    }
}
