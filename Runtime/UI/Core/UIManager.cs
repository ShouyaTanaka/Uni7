using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Uni7.UI
{
    [DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour
    {
        [SerializeField] private UIRegistry registry;
        [SerializeField] private Transform windowRoot;
        [SerializeField] private Transform modalRoot;
        [SerializeField] private bool preloadInitialPoolOnStart = true;

        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> prefabHandles = new Dictionary<string, AsyncOperationHandle<GameObject>>();
        private readonly Dictionary<string, UIPool> pools = new Dictionary<string, UIPool>();
        private readonly Dictionary<string, SemaphoreSlim> operationLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly Dictionary<string, UIBase> active = new Dictionary<string, UIBase>();
        private readonly List<string> modalOrder = new List<string>();
        private CancellationToken destroyToken;
        private bool shuttingDown;

        public static UIManager Instance { get; private set; }
        public IReadOnlyDictionary<string, UIBase> ActiveUIs => active;

        /// <summary>
        /// Runtime bootstrap用。SceneにManagerを配置せず、起動時に生成したCanvasへ接続します。
        /// </summary>
        public void ConfigureRuntime(UIRegistry uiRegistry, Transform windows, Transform modals)
        {
            registry = uiRegistry;
            if (windows != null) windowRoot = windows;
            if (modals != null) modalRoot = modals;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Uni7 UI] UIManagerはシーン内に1つだけ配置してください。", this);
                enabled = false;
                return;
            }
            Instance = this;
            destroyToken = this.GetCancellationTokenOnDestroy();
            if (windowRoot == null) windowRoot = transform;
            if (modalRoot == null) modalRoot = transform;
        }

        private void OnEnable()
        {
            UIEventHub.OpenWindow += HandleOpenWindow;
            UIEventHub.OpenModal += HandleOpenModal;
            UIEventHub.CloseUI += HandleCloseUI;
            UIEventHub.CloseTopModal += HandleCloseTopModal;
            UIEventHub.CloseAll += HandleCloseAll;
        }

        private void Start()
        {
            if (preloadInitialPoolOnStart) PreloadInitialPoolsAsync(destroyToken).Forget(DebugException);
        }

        private void OnDisable()
        {
            UIEventHub.OpenWindow -= HandleOpenWindow;
            UIEventHub.OpenModal -= HandleOpenModal;
            UIEventHub.CloseUI -= HandleCloseUI;
            UIEventHub.CloseTopModal -= HandleCloseTopModal;
            UIEventHub.CloseAll -= HandleCloseAll;
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            if (Instance == this) Instance = null;
            foreach (var pair in active)
                if (pair.Value != null) Destroy(pair.Value.gameObject);
            active.Clear();
            foreach (var pair in pools) pair.Value.DisposeAll();
            pools.Clear();
            foreach (var pair in prefabHandles)
                if (pair.Value.IsValid()) Addressables.Release(pair.Value);
            prefabHandles.Clear();
        }

        public async UniTask<T> OpenWindowAsync<T>(string id, CancellationToken cancellationToken = default) where T : UIWindow
        {
            UIBase result = await OpenAsync(id, UIType.Window, cancellationToken);
            return result as T;
        }

        public async UniTask<UIWindow> OpenWindowAsync(string id, CancellationToken cancellationToken = default)
        {
            return (UIWindow)await OpenAsync(id, UIType.Window, cancellationToken);
        }

        public async UniTask<UIModal> OpenModalAsync(string id, CancellationToken cancellationToken = default)
        {
            return (UIModal)await OpenAsync(id, UIType.Modal, cancellationToken);
        }

        private async UniTask<UIBase> OpenAsync(string id, UIType expectedType, CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("UI IDが空です。", nameof(id));
            SemaphoreSlim gate = GetOperationLock(id);
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(destroyToken, cancellationToken))
            {
                await gate.WaitAsync(linked.Token);
                try { return await OpenInternalAsync(id, expectedType, linked.Token); }
                finally { gate.Release(); }
            }
        }

        private async UniTask<UIBase> OpenInternalAsync(string id, UIType expectedType, CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            if (registry == null || !registry.TryGet(id, out UIEntry entry))
                throw new KeyNotFoundException($"[Uni7 UI] UIRegistryにID '{id}' が登録されていません。");
            if (entry.type != expectedType)
                throw new InvalidOperationException($"[Uni7 UI] '{id}' の種類は {entry.type} です。要求された種類: {expectedType}");

            if (active.TryGetValue(id, out UIBase alreadyOpen) && alreadyOpen != null)
            {
                if (alreadyOpen.State == UIState.Open || alreadyOpen.State == UIState.Opening)
                    return alreadyOpen;
                if (alreadyOpen.State == UIState.Closing)
                    throw new InvalidOperationException($"[Uni7 UI] '{id}' はClose中です。Close完了後に再度Openしてください。");
            }

            GameObject prefab = await LoadPrefabAsync(entry, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            UIBase instance = GetFromPool(id);
            if (instance == null)
            {
                GameObject go = Instantiate(prefab, expectedType == UIType.Window ? windowRoot : modalRoot);
                instance = go.GetComponent<UIBase>();
                if (instance == null)
                {
                    Destroy(go);
                    throw new InvalidOperationException($"[Uni7 UI] Prefab '{entry.address}' にUIBase / UIWindow / UIModalがありません。");
                }
            }

            try { ValidateType(id, expectedType, instance); }
            catch
            {
                if (instance != null) Destroy(instance.gameObject);
                throw;
            }
            instance.transform.SetParent(expectedType == UIType.Window ? windowRoot : modalRoot, false);
            instance.UIId = id;
            instance.State = UIState.Opening;
            instance.SetInputEnabled(false);
            instance.CanvasGroup.alpha = 0f;
            active[id] = instance;
            instance.gameObject.SetActive(true);
            if (expectedType == UIType.Modal)
            {
                modalOrder.Remove(id);
                modalOrder.Add(id);
                instance.transform.SetAsLastSibling();
            }

            try
            {
                await instance.OnInitializeAsync();
                await PlayAnimationAsync(instance, entry, true, cancellationToken);
                instance.State = UIState.Open;
                instance.SetInputEnabled(true);
                await instance.OnOpenedAsync();
                return instance;
            }
            catch
            {
                active.Remove(id);
                modalOrder.Remove(id);
                ReturnToPool(id, instance, entry);
                throw;
            }
        }

        public async UniTask<bool> CloseUIAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id)) return false;
            SemaphoreSlim gate = GetOperationLock(id);
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(destroyToken, cancellationToken))
            {
                await gate.WaitAsync(linked.Token);
                try { return await CloseInternalAsync(id, linked.Token); }
                finally { gate.Release(); }
            }
        }

        private async UniTask<bool> CloseInternalAsync(string id, CancellationToken cancellationToken)
        {
            if (!active.TryGetValue(id, out UIBase instance) || instance == null) return false;
            if (instance.State == UIState.Closing || instance.State == UIState.Pooled) return false;
            if (registry == null || !registry.TryGet(id, out UIEntry entry)) return false;

            instance.State = UIState.Closing;
            instance.SetInputEnabled(false);
            try
            {
                await instance.OnClosingAsync();
                await PlayAnimationAsync(instance, entry, false, cancellationToken);
            }
            catch
            {
                if (!shuttingDown && instance != null)
                {
                    instance.State = UIState.Open;
                    instance.SetInputEnabled(true);
                }
                throw;
            }

            active.Remove(id);
            modalOrder.Remove(id);
            ReturnToPool(id, instance, entry);
            return true;
        }

        public async UniTask CloseTopModalAsync(CancellationToken cancellationToken = default)
        {
            for (int i = modalOrder.Count - 1; i >= 0; i--)
            {
                string id = modalOrder[i];
                if (active.ContainsKey(id))
                {
                    await CloseUIAsync(id, cancellationToken);
                    return;
                }
            }
        }

        public async UniTask CloseAllWindowsAsync(CancellationToken cancellationToken = default)
        {
            string[] ids = new string[active.Count];
            active.Keys.CopyTo(ids, 0);
            foreach (string id in ids)
            {
                if (registry != null && registry.TryGet(id, out UIEntry entry) && entry.type == UIType.Window)
                    await CloseUIAsync(id, cancellationToken);
            }
        }

        public async UniTask CloseAllAsync(CancellationToken cancellationToken = default)
        {
            string[] ids = new string[active.Count];
            active.Keys.CopyTo(ids, 0);
            // Modalを先に閉じ、最後にWindowを閉じる。
            Array.Sort(ids, (a, b) =>
            {
                if (registry == null) return 0;
                registry.TryGet(a, out UIEntry ea);
                registry.TryGet(b, out UIEntry eb);
                return (ea != null && ea.type == UIType.Modal ? 0 : 1).CompareTo(eb != null && eb.type == UIType.Modal ? 0 : 1);
            });
            foreach (string id in ids) await CloseUIAsync(id, cancellationToken);
        }

        public UIBase GetUI(string id) => active.TryGetValue(id, out UIBase ui) ? ui : null;

        public async UniTask PreloadAsync(string id, int poolCount = 0, CancellationToken cancellationToken = default)
        {
            if (registry == null || !registry.TryGet(id, out UIEntry entry))
                throw new KeyNotFoundException($"[Uni7 UI] UIRegistryにID '{id}' が登録されていません。");
            GameObject prefab = await LoadPrefabAsync(entry, cancellationToken);
            int count = Mathf.Clamp(poolCount, 0, entry.maxPoolSize);
            for (int i = GetPool(id).Count; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameObject pooledObject = Instantiate(prefab, entry.type == UIType.Window ? windowRoot : modalRoot);
                UIBase instance = pooledObject.GetComponent<UIBase>();
                if (instance == null)
                {
                    Destroy(pooledObject);
                    throw new InvalidOperationException($"[Uni7 UI] Prefab '{entry.address}' にUIBaseがありません。");
                }
                ValidateType(id, entry.type, instance);
                instance.UIId = id;
                instance.State = UIState.Pooled;
                instance.SetInputEnabled(false);
                instance.gameObject.SetActive(false);
                GetPool(id).Push(instance);
            }
        }

        public async UniTask ReleasePoolAsync(string id)
        {
            if (active.ContainsKey(id))
                throw new InvalidOperationException($"[Uni7 UI] 表示中のUI '{id}' のPoolは解放できません。");
            if (pools.TryGetValue(id, out UIPool pool)) pool.DisposeAll();
            pools.Remove(id);
            if (prefabHandles.TryGetValue(id, out AsyncOperationHandle<GameObject> handle))
            {
                prefabHandles.Remove(id);
                if (handle.IsValid()) Addressables.Release(handle);
            }
            await UniTask.CompletedTask;
        }

        private async UniTask<GameObject> LoadPrefabAsync(UIEntry entry, CancellationToken token)
        {
            if (!prefabHandles.TryGetValue(entry.id, out AsyncOperationHandle<GameObject> handle) || !handle.IsValid())
            {
                handle = Addressables.LoadAssetAsync<GameObject>(entry.address);
                prefabHandles[entry.id] = handle; // pending中も登録し、同一Addressの多重ロードを防ぐ。
            }

            try
            {
                GameObject prefab = await handle.Task.AsUniTask().AttachExternalCancellation(token);
                if (prefab == null) throw new InvalidOperationException($"[Uni7 UI] AddressablesからPrefabを取得できません: {entry.address}");
                return prefab;
            }
            catch (OperationCanceledException)
            {
                // 呼び出し元だけをキャンセル。共有ロードHandleは他のOpen要求のため保持する。
                throw;
            }
            catch
            {
                if (prefabHandles.TryGetValue(entry.id, out AsyncOperationHandle<GameObject> current) && current.Equals(handle))
                {
                    prefabHandles.Remove(entry.id);
                    if (handle.IsValid()) Addressables.Release(handle);
                }
                throw;
            }
        }

        private SemaphoreSlim GetOperationLock(string id)
        {
            if (!operationLocks.TryGetValue(id, out SemaphoreSlim gate))
                operationLocks[id] = gate = new SemaphoreSlim(1, 1);
            return gate;
        }

        private UIBase GetFromPool(string id)
        {
            if (pools.TryGetValue(id, out UIPool pool) && pool.TryPop(out UIBase instance)) return instance;
            return null;
        }

        private UIPool GetPool(string id)
        {
            if (!pools.TryGetValue(id, out UIPool pool)) pools[id] = pool = new UIPool();
            return pool;
        }

        private void ReturnToPool(string id, UIBase instance, UIEntry entry)
        {
            if (instance == null) return;
            instance.OnReturnedToPool();
            instance.State = UIState.Pooled;
            instance.SetInputEnabled(false);
            instance.gameObject.SetActive(false);
            int max = entry != null ? Mathf.Max(0, entry.maxPoolSize) : 0;
            if (GetPool(id).Count < max) GetPool(id).Push(instance);
            else Destroy(instance.gameObject);
        }

        private async UniTask PreloadInitialPoolsAsync(CancellationToken token)
        {
            if (registry == null) return;
            foreach (UIEntry entry in registry.Entries)
                if (entry != null && entry.initialPoolSize > 0)
                    await PreloadAsync(entry.id, entry.initialPoolSize, token);
        }

        private async UniTask PlayAnimationAsync(UIBase ui, UIEntry entry, bool opening, CancellationToken external)
        {
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(destroyToken, external))
                await UIAnimationController.PlayAsync(ui, entry, opening, linked.Token);
        }

        private void ValidateType(string id, UIType type, UIBase instance)
        {
            bool valid = type == UIType.Window ? instance is UIWindow : instance is UIModal;
            if (!valid) throw new InvalidOperationException($"[Uni7 UI] '{id}' のPrefabは {type} 用の基底クラスを継承してください。");
        }
        private void ThrowIfUnavailable()
        {
            if (shuttingDown || !isActiveAndEnabled) throw new InvalidOperationException("[Uni7 UI] UIManagerは利用できない状態です。");
        }
        private void HandleOpenWindow(string id) => OpenWindowAsync(id, destroyToken).Forget(DebugException);
        private void HandleOpenModal(string id) => OpenModalAsync(id, destroyToken).Forget(DebugException);
        private void HandleCloseUI(string id) => CloseUIAsync(id, destroyToken).Forget(DebugException);
        private void HandleCloseTopModal() => CloseTopModalAsync(destroyToken).Forget(DebugException);
        private void HandleCloseAll() => CloseAllAsync(destroyToken).Forget(DebugException);
        private static void DebugException(Exception exception) => Debug.LogException(exception);
    }
}
