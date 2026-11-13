using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Uni7.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIBase : MonoBehaviour
    {
        [NonSerialized] private CanvasGroup canvasGroup;

        [SerializeField, HideInInspector]
        private string uiId;

        public string UIId
        {
            get => uiId;
            internal set => uiId = value;
        }

        public UIState State { get; internal set; } = UIState.None;
        public CanvasGroup CanvasGroup => canvasGroup != null ? canvasGroup : (canvasGroup = GetComponent<CanvasGroup>());

        protected virtual void Awake() { canvasGroup = GetComponent<CanvasGroup>(); }
        public virtual UniTask OnInitializeAsync() => UniTask.CompletedTask;
        public virtual UniTask OnOpenedAsync() => UniTask.CompletedTask;
        public virtual UniTask OnClosingAsync() => UniTask.CompletedTask;
        public virtual void OnReturnedToPool() { }

        internal void SetInputEnabled(bool enabled)
        {
            CanvasGroup.interactable = enabled;
            CanvasGroup.blocksRaycasts = enabled;
        }
    }

    public enum UIState { None, Loading, Opening, Open, Closing, Pooled }
}
