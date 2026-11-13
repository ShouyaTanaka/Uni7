using System.Collections.Generic;
using UnityEngine;

namespace Uni7.UI
{
    [CreateAssetMenu(fileName = "UIRegistry", menuName = "Uni7/UI Registry")]
    public sealed class UIRegistry : ScriptableObject
    {
        [SerializeField] private List<UIEntry> entries = new List<UIEntry>();
        public IReadOnlyList<UIEntry> Entries => entries;

        public bool TryGet(string id, out UIEntry entry)
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
        public List<UIEntry> EditorEntries => entries;
#endif
    }
}
