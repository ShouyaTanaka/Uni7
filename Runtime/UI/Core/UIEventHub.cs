using System;

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

        public static void PublishOpenWindow(string id) => OpenWindow?.Invoke(id);
        public static void PublishOpenModal(string id) => OpenModal?.Invoke(id);
        public static void PublishCloseUI(string id) => CloseUI?.Invoke(id);
        public static void PublishCloseTopModal() => CloseTopModal?.Invoke();
        public static void PublishCloseAll() => CloseAll?.Invoke();
    }
}
