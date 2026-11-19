using System;
using System.Collections.Generic;
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
        [SerializeField] private bool closeCurrentBeforeOpen = true;
        [SerializeField] private string eventCategory;
        [SerializeField] private string eventName;
        [SerializeField] private EventParameterType parameterType;
        [SerializeField] private string parameterValue;
        [SerializeField] private GameObject objectParameter;
        [SerializeField] private List<EventParameterValue> parameters = new List<EventParameterValue>();
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
            List<EventParameterValue> configured = entry != null ? entry.parameters : parameters;

            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(customEventName))
                throw new InvalidOperationException("ButtonのEventカテゴリまたはEvent名が未設定です。");

            // Backward compatibility: old ButtonRegistry / Prefab data stored one argument in legacy fields.
            if (configured == null || configured.Count == 0)
            {
                EventParameterType legacyType = entry != null ? entry.parameterType : parameterType;
                if (legacyType != EventParameterType.None)
                {
                    configured = new List<EventParameterValue>
                    {
                        new EventParameterValue
                        {
                            parameterType = legacyType,
                            value = entry != null ? entry.parameterValue : parameterValue,
                            objectValue = entry != null ? entry.objectParameter : objectParameter
                        }
                    };
                }
                else configured = new List<EventParameterValue>();
            }

            Type[] managerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("EventManager", false))
                .Where(typeInfo => typeInfo != null)
                .ToArray();
            if (managerTypes.Length == 0)
                throw new InvalidOperationException("EventManagerが見つかりません。Event Generatorでコードを生成してください。");
            if (managerTypes.Length > 1)
                throw new InvalidOperationException("EventManagerが複数見つかりました。同名型の重複を解消してください。");

            Type managerType = managerTypes[0];
            PropertyInfo categoryProperty = managerType.GetProperty(categoryName, BindingFlags.Public | BindingFlags.Static);
            object category = categoryProperty?.GetValue(null);
            if (category == null) throw new InvalidOperationException($"Eventカテゴリ '{categoryName}' が見つかりません。");

            if (configured.Any(value => value == null))
                throw new InvalidOperationException($"Event '{categoryName}.{customEventName}' の引数設定に空の要素があります。UIButtonRegistryを再設定してください。");

            Type[] expectedTypes = configured.Select(value => GetParameterType(value.parameterType)).ToArray();
            string methodName = "Emit" + customEventName;
            MethodInfo[] candidates = category.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => candidate.Name == methodName)
                .Where(candidate => candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(expectedTypes))
                .ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException($"Event '{categoryName}.{customEventName}' が見つからないか、引数定義が一致しません。Event Generatorで再生成してください。");
            if (candidates.Length > 1)
                throw new InvalidOperationException($"Event '{categoryName}.{customEventName}' のEmitメソッドが曖昧です。");

            object[] args = configured.Select(ParseParameter).ToArray();
            candidates[0].Invoke(category, args);
        }

        private static Type GetParameterType(EventParameterType type)
        {
            switch (type)
            {
                case EventParameterType.None: return null;
                case EventParameterType.Int: return typeof(int);
                case EventParameterType.Float: return typeof(float);
                case EventParameterType.String: return typeof(string);
                case EventParameterType.Bool: return typeof(bool);
                case EventParameterType.Vector2: return typeof(Vector2);
                case EventParameterType.Vector3: return typeof(Vector3);
                case EventParameterType.GameObject: return typeof(GameObject);
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "未対応のEvent引数型です。");
            }
        }

        private static object ParseParameter(EventParameterValue parameter)
        {
            string value = parameter.value;
            switch (parameter.parameterType)
            {
                case EventParameterType.String: return value ?? string.Empty;
                case EventParameterType.Int: return int.Parse(string.IsNullOrWhiteSpace(value) ? "0" : value, System.Globalization.CultureInfo.InvariantCulture);
                case EventParameterType.Float: return float.Parse(string.IsNullOrWhiteSpace(value) ? "0" : value, System.Globalization.CultureInfo.InvariantCulture);
                case EventParameterType.Bool: return bool.Parse(string.IsNullOrWhiteSpace(value) ? "false" : value);
                case EventParameterType.Vector2:
                    {
                        string[] parts = (value ?? "").Split(',');
                        if (parts.Length != 2) throw new FormatException("Vector2は 'x,y' 形式で入力してください。");
                        return new Vector2(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    }
                case EventParameterType.Vector3:
                    {
                        string[] parts = (value ?? "").Split(',');
                        if (parts.Length != 3) throw new FormatException("Vector3は 'x,y,z' 形式で入力してください。");
                        return new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                    }
                case EventParameterType.GameObject: return parameter.objectValue;
                default: throw new ArgumentOutOfRangeException(nameof(parameter.parameterType), parameter.parameterType, "未対応のEvent引数型です。");
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
