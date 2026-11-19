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
            float originalAlpha = group.alpha;
            Vector3 originalScale = ui.PrefabScale;
            Vector3 hiddenScale = entry != null && entry.useScaleAnimation
                ? originalScale * 0.94f
                : originalScale;
            float from = opening ? 0f : 1f;
            float to = opening ? 1f : 0f;

            try
            {
                group.alpha = from;
                if (entry != null && entry.useScaleAnimation)
                    target.localScale = opening ? hiddenScale : originalScale;

                if (duration <= 0f)
                {
                    group.alpha = to;
                    target.localScale = opening ? originalScale : hiddenScale;
                    return;
                }

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    token.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    t = t * t * (3f - 2f * t);
                    group.alpha = Mathf.Lerp(from, to, t);
                    if (entry != null && entry.useScaleAnimation)
                        target.localScale = Vector3.Lerp(
                            opening ? hiddenScale : originalScale,
                            opening ? originalScale : hiddenScale, t);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                group.alpha = to;
                target.localScale = opening ? originalScale : hiddenScale;
            }
            catch
            {
                // UIManager restores its logical state on failure; restore the matching visual state.
                if (ui != null)
                {
                    group.alpha = originalAlpha;
                    target.localScale = originalScale;
                }
                throw;
            }
        }
    }
}
