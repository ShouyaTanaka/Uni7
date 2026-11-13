using System;
using UnityEngine;

namespace Uni7.UI
{
    public enum UIType { Window, Modal }

    [Serializable]
    public sealed class UIEntry
    {
        [Tooltip("Codeから指定する一意のUI ID")]
        public string id;
        [Tooltip("AddressablesのAddress。通常はIDと同じ値にします")]
        public string address;
        [Tooltip("Editor上でPrefabを追跡するためのAsset Path")]
        public string prefabPath;
        [Tooltip("生成時またはユーザーが確認した時点のPrefab識別情報")]
        public string prefabGuid;
        public string prefabDependencyHash;
        public UIType type;
        [Min(0)] public int initialPoolSize = 0;
        [Min(0)] public int maxPoolSize = 2;
        [Min(0f)] public float animationDuration = 0.18f;
        public bool useScaleAnimation = true;
    }
}
