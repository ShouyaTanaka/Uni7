using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Uni7.UI
{
    internal static class UIAnimationController
    {
        public static async UniTask PlayAsync(UIBase ui, UIEntry entry, bool opening, CancellationToken token)
        {
            float duration = entry != null ? entry.animationDuration : 0.18f;
            CanvasGroup group = ui.CanvasGroup;
            Transform target = ui.transform;
            Vector3 shownScale = target.localScale;
            Vector3 hiddenScale = entry != null && entry.useScaleAnimation ? Vector3.one * 0.94f : shownScale;
            float from = opening ? 0f : 1f;
            float to = opening ? 1f : 0f;
            group.alpha = from;
            if (entry != null && entry.useScaleAnimation) target.localScale = opening ? hiddenScale : shownScale;
            if (duration <= 0f)
            {
                group.alpha = to;
                target.localScale = shownScale;
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // SmoothStepで開始・終了を滑らかにする。
                t = t * t * (3f - 2f * t);
                group.alpha = Mathf.Lerp(from, to, t);
                if (entry != null && entry.useScaleAnimation)
                    target.localScale = Vector3.Lerp(opening ? hiddenScale : shownScale, opening ? shownScale : hiddenScale, t);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            group.alpha = to;
            target.localScale = shownScale;
        }
    }
}
