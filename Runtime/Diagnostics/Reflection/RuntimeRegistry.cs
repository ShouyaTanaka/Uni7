using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uni7.Diagnostics
{
    /// <summary>
    /// MonoBehaviour以外の通常クラスのインスタンスをRuntime Invokerへ公開するRegistry。
    /// 登録したインスタンスはEditor WindowのRefresh時に取得されます。
    /// </summary>
    public static class RuntimeRegistry
    {
        private static readonly List<object> Instances = new List<object>();

        public static event Action Changed;

        /// <summary>内部Listを公開せず、呼び出し時点のSnapshotを返します。</summary>
        public static IReadOnlyList<object> RegisteredInstances
        {
            get { return Instances.ToArray(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Instances.Clear();
            Changed = null;
        }

        public static void Register(object instance)
        {
            if (instance == null) return;
            if (instance is UnityEngine.Object unityObject && unityObject == null) return;
            for (int i = 0; i < Instances.Count; i++)
                if (ReferenceEquals(Instances[i], instance)) return;

            Instances.Add(instance);
            NotifyChanged();
        }

        /// <summary>登録解除をDisposeで行えるスコープ付き登録。</summary>
        public static IDisposable RegisterScoped(object instance)
        {
            Register(instance);
            return new Registration(instance);
        }

        public static void Unregister(object instance)
        {
            if (ReferenceEquals(instance, null)) return;
            for (int i = Instances.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(Instances[i], instance)) continue;
                Instances.RemoveAt(i);
                NotifyChanged();
                return;
            }
        }

        public static void Clear()
        {
            if (Instances.Count == 0) return;
            Instances.Clear();
            NotifyChanged();
        }

        private static void NotifyChanged()
        {
            Action handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate callback in handlers.GetInvocationList())
            {
                try { ((Action)callback)(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private sealed class Registration : IDisposable
        {
            private object instance;
            public Registration(object value) { instance = value; }

            public void Dispose()
            {
                object value = instance;
                if (ReferenceEquals(value, null)) return;
                instance = null;
                Unregister(value);
            }
        }
    }
}
