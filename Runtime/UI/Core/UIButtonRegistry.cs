using System;
using System.Collections.Generic;
using UnityEngine;
using Uni7.Events;

namespace Uni7.UI
{
    public enum UIButtonAction
    {
        OpenWindow,
        OpenModal,
        CloseCurrent,
        CloseTopModal,
        CloseAll,
        EmitEvent,
        CloseUI
    }

    [Serializable]
    public sealed class UIButtonEntry
    {
        public string id;
        public string label;
        public string prefabPath;
        public string prefabGuid;
        public string prefabDependencyHash;
        public UIButtonAction action;
        public string destinationUIId;
        public bool closeCurrentBeforeOpen = true;
        public string eventCategory;
        public string eventName;
        public EventParameterType parameterType;
        public string parameterValue;
        public GameObject objectParameter;
        public float pressScale = 0.94f;
        public float animationDuration = 0.08f;
    }

    [CreateAssetMenu(fileName = "UIButtonRegistry", menuName = "Uni7/UI Button Registry")]
    public sealed class UIButtonRegistry : ScriptableObject
    {
        [SerializeField] private List<UIButtonEntry> entries = new List<UIButtonEntry>();
        public IReadOnlyList<UIButtonEntry> Entries => entries;

        public bool TryGet(string id, out UIButtonEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].id == id)
                {
                    entry = entries[i];
                    return true;
                }
            }
            return false;
        }
#if UNITY_EDITOR
        public List<UIButtonEntry> EditorEntries => entries;
#endif
    }
}
