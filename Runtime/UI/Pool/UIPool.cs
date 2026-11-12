using System.Collections.Generic;
using UnityEngine;

namespace Uni7.UI
{
    internal sealed class UIPool
    {
        private readonly Stack<UIBase> available = new Stack<UIBase>();
        public int Count => available.Count;
        public void Push(UIBase item) { if (item != null) available.Push(item); }
        public bool TryPop(out UIBase item)
        {
            while (available.Count > 0)
            {
                item = available.Pop();
                if (item != null) return true;
            }
            item = null;
            return false;
        }
        public void DisposeAll()
        {
            while (available.Count > 0)
            {
                UIBase item = available.Pop();
                if (item != null) Object.Destroy(item.gameObject);
            }
        }
    }
}
