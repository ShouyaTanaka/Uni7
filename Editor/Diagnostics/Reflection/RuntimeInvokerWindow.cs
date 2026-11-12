using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uni7.Diagnostics
{
    public sealed class RuntimeInvokerWindow : EditorWindow
    {
        private const string WindowTitle = "Runtime Invoker";

        private readonly List<MethodEntry> _methods = new List<MethodEntry>();
        private readonly List<TargetEntry> _targets = new List<TargetEntry>();
        private readonly HashSet<string> _favorites = new HashSet<string>();

        private string _search = string.Empty;
        private string _selectedTypeKey;
        private MethodEntry _selectedMethod;
        private VisualElement _root;
        private VisualElement _navigation;
        private VisualElement _methodList;
        private VisualElement _details;
        private Label _status;
        private bool _showFavoritesOnly;

        [MenuItem("Tools/Uni7/Diagnostics/Runtime Invoker")]
        public static void Open()
        {
            var window = GetWindow<RuntimeInvokerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(850, 500);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            BuildUI();
            RuntimeRegistry.Changed += OnRegistryChanged;
        }

        private void OnDisable()
        {
            RuntimeRegistry.Changed -= OnRegistryChanged;
        }

        private void OnRegistryChanged()
        {
            SetStatus("Registry changed. Press Refresh to update the target list.");
        }

        private void BuildUI()
        {
            _root = rootVisualElement;
            _root.Clear();
            _root.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            var refreshButton = new ToolbarButton(Refresh) { text = "Refresh" };
            toolbar.Add(refreshButton);

            var playState = new Label();
            playState.style.marginLeft = 8;
            playState.text = EditorApplication.isPlaying ? "Play Mode" : "Not Playing";
            toolbar.Add(playState);

            var searchField = new ToolbarSearchField();
            searchField.style.flexGrow = 1;
            searchField.tooltip = "Search namespace, class, category or method";
            searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RebuildLists();
            });
            toolbar.Add(searchField);

            var favoritesToggle = new ToolbarToggle { text = "Favorites" };
            favoritesToggle.RegisterValueChangedCallback(evt =>
            {
                _showFavoritesOnly = evt.newValue;
                RebuildLists();
            });
            toolbar.Add(favoritesToggle);

            _root.Add(toolbar);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            _root.Add(body);

            _navigation = new ScrollView();
            _navigation.style.width = 220;
            _navigation.style.minWidth = 180;
            _navigation.style.flexShrink = 0;
            _navigation.style.borderRightWidth = 1;
            _navigation.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            body.Add(_navigation);

            _methodList = new ScrollView();
            _methodList.style.width = 300;
            _methodList.style.minWidth = 240;
            _methodList.style.flexShrink = 0;
            _methodList.style.borderRightWidth = 1;
            _methodList.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            body.Add(_methodList);

            _details = new ScrollView();
            _details.style.flexGrow = 1;
            _details.style.paddingLeft = 12;
            _details.style.paddingRight = 12;
            _details.style.paddingTop = 8;
            body.Add(_details);

            _status = new Label("Press Refresh to scan [CanReflection] targets.");
            _status.style.paddingLeft = 8;
            _status.style.paddingTop = 4;
            _status.style.paddingBottom = 4;
            _status.style.color = Color.gray;
            _root.Add(_status);

            ShowWelcome();
        }

        private void Refresh()
        {
            _methods.Clear();
            _targets.Clear();
            _selectedMethod = null;
            _selectedTypeKey = null;

            if (!EditorApplication.isPlaying)
            {
                SetStatus("Enter Play Mode, then press Refresh.");
                RebuildLists();
                ShowWelcome();
                return;
            }

            try
            {
                CollectAttributedMethods();
                CollectTargets();
                BuildMethodEntries();
                SetStatus($"Updated: {_methods.Count} methods / {_targets.Count} targets. (Manual refresh only)");
                RebuildLists();
                ShowWelcome();
            }
            catch (Exception ex)
            {
                UTLog.LogException(ex);
                SetStatus("Refresh failed. See Console for details.");
            }
        }

        private void CollectAttributedMethods()
        {
            var classTypes = TypeCache.GetTypesWithAttribute<CanReflectionAttribute>();
            var methodInfos = TypeCache.GetMethodsWithAttribute<CanReflectionAttribute>();

            var classSet = new HashSet<Type>();
            foreach (var type in classTypes)
            {
                if (type != null && !type.IsAbstract && !type.IsGenericTypeDefinition)
                    classSet.Add(type);
            }

            var methods = new Dictionary<string, MethodInfo>();

            foreach (var type in classSet)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                foreach (var method in type.GetMethods(flags))
                {
                    if (IsSupportedMethod(method))
                        methods[GetMethodKey(method)] = method;
                }
            }

            foreach (var method in methodInfos)
            {
                if (method == null || !IsSupportedMethod(method))
                    continue;

                methods[GetMethodKey(method)] = method;
            }

            _cachedMethods = methods.Values
                .OrderBy(m => m.DeclaringType?.FullName)
                .ThenBy(m => m.Name)
                .ToList();
        }

        private List<MethodInfo> _cachedMethods = new List<MethodInfo>();

        private static bool IsSupportedMethod(MethodInfo method)
        {
            if (method.IsSpecialName || method.IsConstructor || method.ContainsGenericParameters)
                return false;

            if (!(method.IsPublic))
                return false;

            if (method.ReturnType.IsPointer || method.ReturnType.IsByRef)
                return false;

            var parameters = method.GetParameters();
            foreach (var parameter in parameters)
            {
                if (parameter.IsOut || parameter.ParameterType.IsByRef || parameter.IsOptional ||
                    parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                    return false;

                if (!ArgumentDrawer.IsSupported(parameter.ParameterType))
                    return false;
            }

            return true;
        }

        private void CollectTargets()
        {
            // Scene上のMonoBehaviourだけを対象にし、Refresh時に一度だけ検索する。
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (_cachedMethods.Any(m => m.DeclaringType.IsAssignableFrom(type) && !m.IsStatic))
                    AddTarget(behaviour, type);
            }

            foreach (var instance in RuntimeRegistry.RegisteredInstances)
            {
                if (instance == null)
                    continue;

                if (instance is UnityEngine.Object unityObject && unityObject == null)
                    continue;

                var type = instance.GetType();
                if (_cachedMethods.Any(m => m.DeclaringType.IsAssignableFrom(type) && !m.IsStatic))
                    AddTarget(instance, type);
            }

            // Static methodはインスタンス不要。型情報だけのTargetEntryを作成する。
            foreach (var method in _cachedMethods.Where(m => m.IsStatic))
            {
                if (!_targets.Any(t => t.Type == method.DeclaringType && t.Instance == null))
                    _targets.Add(new TargetEntry(method.DeclaringType, null));
            }
        }

        private void AddTarget(object instance, Type type)
        {
            if (_targets.Any(t => ReferenceEquals(t.Instance, instance)))
                return;

            _targets.Add(new TargetEntry(type, instance));
        }

        private void BuildMethodEntries()
        {
            foreach (var target in _targets)
            {
                foreach (var method in _cachedMethods)
                {
                    if (method.IsStatic)
                    {
                        if (method.DeclaringType != target.Type || target.Instance != null)
                            continue;
                    }
                    else
                    {
                        if (target.Instance == null || !method.DeclaringType.IsAssignableFrom(target.Type))
                            continue;
                    }

                    var methodAttribute = method.GetCustomAttribute<CanReflectionAttribute>();
                    var classAttribute = method.DeclaringType.GetCustomAttribute<CanReflectionAttribute>();
                    var category = methodAttribute != null
                        ? methodAttribute.Category
                        : classAttribute != null ? classAttribute.Category : "General";

                    _methods.Add(new MethodEntry(method, target, category));
                }
            }
        }

        private void RebuildLists()
        {
            if (_navigation == null || _methodList == null)
                return;

            _navigation.Clear();
            _methodList.Clear();

            var filtered = GetFilteredMethods();
            var namespaceGroups = filtered
                .GroupBy(e => string.IsNullOrEmpty(e.Method.DeclaringType.Namespace)
                    ? "(No Namespace)"
                    : e.Method.DeclaringType.Namespace)
                .OrderBy(g => g.Key);

            foreach (var nsGroup in namespaceGroups)
            {
                var nsFoldout = new Foldout { text = nsGroup.Key, value = true };
                nsFoldout.style.marginBottom = 3;

                foreach (var typeGroup in nsGroup.GroupBy(e => e.Method.DeclaringType)
                             .OrderBy(g => g.Key.Name))
                {
                    var typeButton = new Button(() =>
                    {
                        _selectedTypeKey = typeGroup.Key.AssemblyQualifiedName;
                        _selectedMethod = null;
                        RebuildLists();
                        ShowWelcome();
                    })
                    {
                        text = typeGroup.Key.Name
                    };
                    typeButton.style.unityTextAlign = TextAnchor.MiddleLeft;
                    typeButton.style.marginLeft = 12;
                    typeButton.style.marginRight = 4;
                    if (_selectedTypeKey == typeGroup.Key.AssemblyQualifiedName)
                        typeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                    nsFoldout.Add(typeButton);
                }

                _navigation.Add(nsFoldout);
            }

            var selectedEntries = filtered
                .Where(e => _selectedTypeKey == null ||
                            e.Method.DeclaringType.AssemblyQualifiedName == _selectedTypeKey)
                .OrderBy(e => e.Category)
                .ThenBy(e => e.Method.Name)
                .ToList();

            foreach (var entry in selectedEntries)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var button = new Button(() =>
                {
                    _selectedMethod = entry;
                    ShowMethodDetails(entry);
                    RebuildLists();
                });
                button.text = $"{entry.Method.Name}  ({entry.Category})";
                button.style.flexGrow = 1;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (_selectedMethod == entry)
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(button);

                var favorite = new Toggle { text = "★", value = _favorites.Contains(entry.FavoriteKey) };
                favorite.style.width = 36;
                favorite.style.flexShrink = 0;
                favorite.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _favorites.Add(entry.FavoriteKey);
                    else _favorites.Remove(entry.FavoriteKey);
                });
                row.Add(favorite);
                _methodList.Add(row);
            }

            if (selectedEntries.Count == 0)
                _methodList.Add(new Label("No methods. Check attributes and press Refresh."));
        }

        private List<MethodEntry> GetFilteredMethods()
        {
            IEnumerable<MethodEntry> query = _methods;
            if (_showFavoritesOnly)
                query = query.Where(e => _favorites.Contains(e.FavoriteKey));

            if (!string.IsNullOrWhiteSpace(_search))
            {
                var search = _search.Trim();
                query = query.Where(e =>
                    e.Method.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Method.DeclaringType.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return query.ToList();
        }

        private void ShowWelcome()
        {
            if (_details == null)
                return;

            _details.Clear();
            var title = new Label("Runtime Invoker");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _details.Add(title);
            _details.Add(new Label("Select a class and method to inspect its parameters and invoke it."));
            _details.Add(new Label("Refresh is the only operation that performs discovery. Search and selection use cached metadata."));
            if (!EditorApplication.isPlaying)
                _details.Add(new HelpBox("Enter Play Mode to discover scene instances.", HelpBoxMessageType.Info));
        }

        private void ShowMethodDetails(MethodEntry entry)
        {
            _details.Clear();

            var method = entry.Method;
            var title = new Label(method.Name);
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _details.Add(title);

            _details.Add(new Label($"{method.DeclaringType.FullName}  |  {entry.Category}"));
            _details.Add(new Label(entry.Target.Instance == null
                ? "Target: Static"
                : $"Target: {entry.Target.Type.Name} ({entry.Target.DisplayName})"));

            var args = new object[method.GetParameters().Length];
            var parameterFields = new List<Action>();
            var parameters = method.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
                var index = i;
                var field = ArgumentDrawer.CreateField(parameters[i].Name, parameters[i].ParameterType, value =>
                {
                    args[index] = value;
                });
                args[i] = ArgumentDrawer.GetDefault(parameters[i].ParameterType);
                _details.Add(field);
            }

            var invokeButton = new Button(() => Invoke(entry, args))
            {
                text = "Invoke"
            };
            invokeButton.style.height = 32;
            invokeButton.style.marginTop = 12;
            _details.Add(invokeButton);

            _details.Add(new HelpBox("メソッドはPlay Mode中の状態を変更する可能性があります。実行対象を確認してからInvokeしてください。",
                HelpBoxMessageType.Warning));
        }

        private void Invoke(MethodEntry entry, object[] args)
        {
            if (!EditorApplication.isPlaying)
            {
                SetStatus("Play Mode is required.");
                return;
            }

            try
            {
                var target = entry.Method.IsStatic ? null : entry.Target.Instance;
                if (!entry.Method.IsStatic && target == null)
                {
                    SetStatus("Target instance is no longer available. Press Refresh.");
                    return;
                }

                var result = entry.Method.Invoke(target, args);
                if (entry.Method.ReturnType == typeof(void))
                    SetStatus($"Invoked: {entry.Method.Name}");
                else
                    SetStatus($"Invoked: {entry.Method.Name}  |  Result: {result ?? "null"}");
            }
            catch (TargetInvocationException ex)
            {
                var actual = ex.InnerException ?? ex;
                UTLog.LogException(actual);
                SetStatus($"Method threw an exception: {actual.GetType().Name}: {actual.Message}");
            }
            catch (Exception ex)
            {
                UTLog.LogException(ex);
                SetStatus($"Invoke failed: {ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            if (_status != null)
                _status.text = message;
        }

        private static string GetMethodKey(MethodInfo method)
        {
            return $"{method.DeclaringType?.AssemblyQualifiedName}|{method.Name}|{string.Join(",", method.GetParameters().Select(p => p.ParameterType.AssemblyQualifiedName))}";
        }

        private sealed class TargetEntry
        {
            public Type Type { get; }
            public object Instance { get; }
            public string DisplayName => Instance is UnityEngine.Object obj ? obj.name : Type.Name;

            public TargetEntry(Type type, object instance)
            {
                Type = type;
                Instance = instance;
            }
        }

        private sealed class MethodEntry
        {
            public MethodInfo Method { get; }
            public TargetEntry Target { get; }
            public string Category { get; }
            public string FavoriteKey { get; }

            public MethodEntry(MethodInfo method, TargetEntry target, string category)
            {
                Method = method;
                Target = target;
                Category = category;
                FavoriteKey = GetMethodKey(method);
            }
        }
    }
}
