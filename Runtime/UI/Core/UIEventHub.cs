using System;
using UnityEngine;

namespace Uni7.UI
{
    /// <summary>Generated UIEvents forwards UI category events here so the package runtime does not depend on generated scripts.</summary>
    public static class UIEventHub
    {
        public static event Action<string> OpenWindow;
        public static event Action<string> OpenModal;
        public static event Action<string> CloseUI;
        public static event Action CloseTopModal;
        public static event Action CloseAll;

        public static void PublishOpenWindow(string id) => InvokeSafely(OpenWindow, id);
        public static void PublishOpenModal(string id) => InvokeSafely(OpenModal, id);
        public static void PublishCloseUI(string id) => InvokeSafely(CloseUI, id);
        public static void PublishCloseTopModal() => InvokeSafely(CloseTopModal);
        public static void PublishCloseAll() => InvokeSafely(CloseAll);

        private static void InvokeSafely<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try { ((Action<T>)subscriber).Invoke(value); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private static void InvokeSafely(Action handlers)
        {
            if (handlers == null) return;
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try { ((Action)subscriber).Invoke(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
