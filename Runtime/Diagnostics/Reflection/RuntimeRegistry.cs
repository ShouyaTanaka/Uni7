using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uni7.Diagnostics
{
    /// <summary>
    /// MonoBehaviour以外の通常クラスのインスタンスをRuntime Invokerへ公開するためのRegistry。
    /// 登録したインスタンスはEditor WindowのRefresh時に取得されます。
    /// </summary>
    public static class RuntimeRegistry
    {
        private static readonly List<object> Instances = new List<object>();

        public static event Action Changed;

        public static IReadOnlyList<object> RegisteredInstances => Instances;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Instances.Clear();
            Changed = null;
        }

        public static void Register(object instance)
        {
            if (instance == null)
                return;

            if (instance is UnityEngine.Object unityObject && unityObject == null)
                return;

            if (Instances.Contains(instance))
                return;

            Instances.Add(instance);
            Changed?.Invoke();
        }

        public static void Unregister(object instance)
        {
            if (instance == null)
                return;

            if (Instances.Remove(instance))
                Changed?.Invoke();
        }

        public static void Clear()
        {
            if (Instances.Count == 0)
                return;

            Instances.Clear();
            Changed?.Invoke();
        }
    }
}
