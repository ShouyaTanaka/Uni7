using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Uni7.UI
{
    public enum UIButtonTransition { OpenWindow, OpenModal, CloseCurrent }

    [RequireComponent(typeof(Button))]
    public sealed class UIEventButton : MonoBehaviour
    {
        [SerializeField] private UIButtonTransition transition;
        [SerializeField] private string destinationUIId;
        [SerializeField] private bool closeCurrentBeforeOpen;
        [SerializeField, Min(0f)] private float pressScale = 0.94f;
        [SerializeField, Min(0f)] private float animationDuration = 0.08f;
        private Button button;
        private Vector3 initialScale;

        private void Awake()
        {
            button = GetComponent<Button>();
            initialScale = transform.localScale;
            button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            AnimatePressAsync().Forget();
            if (transition == UIButtonTransition.OpenWindow)
            {
                OpenWindowAsync().Forget();
            }
            else if (transition == UIButtonTransition.OpenModal)
                UIEventHub.PublishOpenModal(destinationUIId);
            else if (transition == UIButtonTransition.CloseCurrent)
            {
                UIBase owner = GetComponentInParent<UIBase>();
                if (owner != null && !string.IsNullOrEmpty(owner.UIId))
                    UIEventHub.PublishCloseUI(owner.UIId);
            }
        }

        private async UniTaskVoid OpenWindowAsync()
        {
            // Window遷移時は現在のWindowを閉じてから次のWindowを開く。
            if (closeCurrentBeforeOpen && UIManager.Instance != null)
                await UIManager.Instance.CloseAllWindowsAsync(this.GetCancellationTokenOnDestroy());
            UIEventHub.PublishOpenWindow(destinationUIId);
        }

        private async UniTaskVoid AnimatePressAsync()
        {
            float duration = animationDuration;
            if (duration <= 0f) return;
            var token = this.GetCancellationTokenOnDestroy();
            await AnimateScaleAsync(initialScale, initialScale * pressScale, duration, token);
            await AnimateScaleAsync(initialScale * pressScale, initialScale, duration, token);
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
