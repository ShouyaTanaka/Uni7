using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Uni7.Diagnostics
{
    public sealed class UTConsoleWindow : EditorWindow
    {
        private readonly HashSet<long> selectedIds = new HashSet<long>();
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private string searchText = string.Empty;
        private string selectedTag = "All";
        private bool showLog = true;
        private bool showWarning = true;
        private bool showError = true;
        private bool showDetails = true;
        private bool paused;
        private bool autoScroll = true;
        private bool collapse;
        private float detailWidth = 0.34f;
        private long lastClickedId = -1;
        private int lastClickedVisibleIndex = -1;
        private double lastClickTime;
        private bool scrollToBottom;
        private bool draggingSplitter;
        private Texture2D logIcon;
        private Texture2D warningIcon;
        private Texture2D errorIcon;
        private const float SplitterWidth = 5f;

        [MenuItem("Tools/Uni7/Diagnostics/Console")]
        public static void ShowWindow()
        {
            UTConsoleWindow window = GetWindow<UTConsoleWindow>("Uni7 Console");
            window.minSize = new Vector2(560, 260);
            window.Show();
        }

        private void OnEnable()
        {
            UTLog.Initialize();
            UTLog.OnLogAdded += HandleLogAdded;
            UTLog.OnLogUpdated += HandleLogUpdated;
            UTLog.OnLogsCleared += HandleLogsCleared;
            LoadIcons();
        }

        private void OnDisable()
        {
            UTLog.OnLogAdded -= HandleLogAdded;
            UTLog.OnLogUpdated -= HandleLogUpdated;
            UTLog.OnLogsCleared -= HandleLogsCleared;
        }

        private void LoadIcons()
        {
            logIcon = EditorGUIUtility.IconContent("console.infoicon").image as Texture2D;
            warningIcon = EditorGUIUtility.IconContent("console.warnicon").image as Texture2D;
            errorIcon = EditorGUIUtility.IconContent("console.erroricon").image as Texture2D;
        }

        private void HandleLogAdded(LogEntry entry)
        {
            if (!paused) Repaint();
            if (autoScroll) scrollToBottom = true;
        }

        private void HandleLogUpdated(LogEntry entry) { Repaint(); }
        private void HandleLogsCleared()
        {
            selectedIds.Clear();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSearchBar();

            if (paused)
                EditorGUILayout.HelpBox("Paused: log collection continues; only this window's repaint is paused.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            DrawLogList();
            if (showDetails) DrawSplitter();
            if (showDetails) DrawDetails();
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
            HandleKeyboard();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                if (EditorUtility.DisplayDialog("Clear Logs", "すべてのログを削除しますか？", "Clear", "Cancel"))
                    UTLog.Clear();
            }

            DrawTypeToggle("Log", showLog, Count(LogType.Log), logIcon, v => showLog = v);
            DrawTypeToggle("Warning", showWarning, Count(LogType.Warning), warningIcon, v => showWarning = v);
            DrawTypeToggle("Error", showError, Count(LogType.Error), errorIcon, v => showError = v);

            GUILayout.Space(5);
            List<LogEntry> all = UTLog.GetAllLogs().ToList();
            List<string> tags = all.Select(x => x.Tag).Where(x => !string.IsNullOrEmpty(x))
                .Distinct().OrderBy(x => x).ToList();
            tags.Insert(0, "All");
            if (!tags.Contains(selectedTag)) selectedTag = "All";
            int tagIndex = Mathf.Max(0, tags.IndexOf(selectedTag));
            int nextTag = EditorGUILayout.Popup(tagIndex, tags.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(110));
            selectedTag = tags[nextTag];

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("More", EditorStyles.toolbarButton))
                ShowToolsMenu();
            collapse = GUILayout.Toggle(collapse, "Collapse", EditorStyles.toolbarButton);
            autoScroll = GUILayout.Toggle(autoScroll, "Auto", EditorStyles.toolbarButton);
            paused = GUILayout.Toggle(paused, "Pause", EditorStyles.toolbarButton);
            showDetails = GUILayout.Toggle(showDetails, "Details", EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeToggle(string label, bool value, int count, Texture2D icon, Action<bool> setter)
        {
            GUIContent content = new GUIContent(count.ToString(), icon, label);
            bool next = GUILayout.Toggle(value, content, EditorStyles.toolbarButton, GUILayout.MinWidth(48));
            if (next != value) setter(next);
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(EditorGUIUtility.IconContent("Search").image, GUILayout.Width(18), GUILayout.Height(18));
            searchText = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField);
            if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) searchText = string.Empty;
            EditorGUILayout.EndHorizontal();
        }

        private List<LogEntry> GetFilteredLogs()
        {
            IEnumerable<LogEntry> query = UTLog.GetAllLogs();
            query = query.Where(entry =>
                (entry.Type == LogType.Log && showLog) ||
                (entry.Type == LogType.Warning && showWarning) ||
                (entry.Type == LogType.Error && showError));

            if (selectedTag != "All") query = query.Where(entry => entry.Tag == selectedTag);
            if (!string.IsNullOrEmpty(searchText))
            {
                string search = searchText.Trim();
                query = query.Where(entry =>
                    (entry.Message ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (entry.Tag ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (entry.StackTrace ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return query.ToList();
        }

        private void DrawLogList()
        {
            float totalWidth = position.width;
            float listWidth = showDetails ? Mathf.Max(180f, totalWidth * (1f - detailWidth) - SplitterWidth) : totalWidth;
            EditorGUILayout.BeginVertical(GUILayout.Width(listWidth), GUILayout.ExpandHeight(true));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);

            List<LogEntry> logs = GetFilteredLogs();
            int i = 0;
            while (i < logs.Count)
            {
                LogEntry entry = logs[i];
                int repeat = 1;
                if (collapse)
                {
                    while (i + repeat < logs.Count && IsSameLog(entry, logs[i + repeat])) repeat++;
                }
                DrawLogRow(entry, i, repeat);
                i += repeat;
            }

            if (scrollToBottom && autoScroll && Event.current.type == EventType.Repaint)
            {
                listScroll.y = float.MaxValue;
                scrollToBottom = false;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static bool IsSameLog(LogEntry a, LogEntry b)
        {
            return a.Type == b.Type && a.Tag == b.Tag && a.Message == b.Message;
        }

        private void DrawLogRow(LogEntry entry, int visibleIndex, int repeat)
        {
            bool selected = selectedIds.Contains(entry.Id);
            Rect rowRect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            Color old = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.24f, 0.42f, 0.66f, 0.8f);
            else if (entry.Type == LogType.Error) GUI.backgroundColor = new Color(0.55f, 0.18f, 0.18f, 0.22f);
            else if (entry.Type == LogType.Warning) GUI.backgroundColor = new Color(0.55f, 0.42f, 0.12f, 0.18f);
            GUI.Box(rowRect, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = old;

            Texture2D icon = entry.Type == LogType.Error ? errorIcon :
                entry.Type == LogType.Warning ? warningIcon : logIcon;
            if (icon != null) GUI.DrawTexture(new Rect(rowRect.x + 5, rowRect.y + 3, 16, 16), icon);
            GUI.Label(new Rect(rowRect.x + 25, rowRect.y + 2, 65, 18), entry.Time, EditorStyles.miniLabel);

            GUIStyle tagStyle = new GUIStyle(EditorStyles.miniLabel);
            tagStyle.normal.textColor = new Color(0.45f, 0.72f, 1f);
            GUI.Label(new Rect(rowRect.x + 91, rowRect.y + 2, 112, 18), "[" + entry.Tag + "]", tagStyle);

            string message = entry.Message ?? string.Empty;
            if (repeat > 1) message += "  (" + repeat + ")";
            GUI.Label(new Rect(rowRect.x + 205, rowRect.y + 2, Mathf.Max(20, rowRect.width - 210), 18),
                message, EditorStyles.miniLabel);

            HandleRowInput(rowRect, entry, visibleIndex);
        }

        private void HandleRowInput(Rect rect, LogEntry entry, int visibleIndex)
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
            {
                if (evt.button == 1)
                {
                    if (!selectedIds.Contains(entry.Id)) SelectSingle(entry.Id);
                    ShowContextMenu();
                    evt.Use();
                    return;
                }

                if (evt.button == 0)
                {
                    bool command = evt.control || evt.command;
                    if (evt.shift && lastClickedVisibleIndex >= 0)
                        SelectRange(GetFilteredLogs(), lastClickedVisibleIndex, visibleIndex, command);
                    else if (command)
                    {
                        if (!selectedIds.Add(entry.Id)) selectedIds.Remove(entry.Id);
                    }
                    else
                        SelectSingle(entry.Id);

                    double now = EditorApplication.timeSinceStartup;
                    if (lastClickedId == entry.Id && now - lastClickTime < 0.32)
                        OpenScriptAtLine(entry);
                    lastClickedId = entry.Id;
                    lastClickedVisibleIndex = visibleIndex;
                    lastClickTime = now;
                    GUI.FocusControl(null);
                    Repaint();
                    evt.Use();
                }
            }
        }

        private void SelectSingle(long id)
        {
            selectedIds.Clear();
            selectedIds.Add(id);
        }

        private void SelectRange(List<LogEntry> logs, int from, int to, bool additive)
        {
            if (!additive) selectedIds.Clear();
            int min = Mathf.Clamp(Mathf.Min(from, to), 0, logs.Count - 1);
            int max = Mathf.Clamp(Mathf.Max(from, to), 0, logs.Count - 1);
            for (int i = min; i <= max; i++) selectedIds.Add(logs[i].Id);
        }

        private void DrawSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterWidth, 1, GUILayout.ExpandHeight(true), GUILayout.Width(SplitterWidth));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                draggingSplitter = true;
                Event.current.Use();
            }
            if (draggingSplitter && Event.current.type == EventType.MouseDrag)
            {
                detailWidth = Mathf.Clamp((position.width - Event.current.mousePosition.x) / position.width, 0.22f, 0.65f);
                Repaint();
                Event.current.Use();
            }
            if (draggingSplitter && Event.current.type == EventType.MouseUp)
            {
                draggingSplitter = false;
                GUIUtility.hotControl = 0;
                Event.current.Use();
            }
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f, 0.8f));
        }

        private void DrawDetails()
        {
            float width = Mathf.Max(170f, position.width * detailWidth);
            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
            List<LogEntry> selected = UTLog.GetAllLogs().Where(x => selectedIds.Contains(x.Id)).ToList();

            if (selected.Count == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("ログを選択すると詳細を表示します", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            else if (selected.Count > 1)
            {
                EditorGUILayout.LabelField("複数選択", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(selected.Count + " logs selected");
                GUILayout.Space(8);
                if (GUILayout.Button("Copy Selected")) CopySelected();
                if (GUILayout.Button("Delete Selected")) DeleteSelected();
                if (GUILayout.Button("Clear Selection")) selectedIds.Clear();
            }
            else
            {
                LogEntry entry = selected[0];
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                EditorGUILayout.LabelField(entry.Type.ToString(), EditorStyles.boldLabel);
                DrawDetailField("Time", entry.Time);
                DrawDetailField("Tag", entry.Tag);
                DrawDetailField("Message", entry.Message);
                if (entry.Context != null)
                {
                    EditorGUILayout.ObjectField("Context", entry.Context, typeof(UnityEngine.Object), true);
                }
                if (!string.IsNullOrEmpty(entry.FilePath))
                {
                    if (GUILayout.Button(Path.GetFileName(entry.FilePath) + ":" + entry.LineNumber, EditorStyles.linkLabel))
                        OpenScriptAtLine(entry);
                }
                EditorGUILayout.LabelField("Stack Trace", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(entry.StackTrace ?? string.Empty, EditorStyles.textArea,
                    GUILayout.MinHeight(100), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawDetailField(string label, string value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.textArea, GUILayout.MinHeight(label == "Message" ? 60 : 20));
        }

        private void DrawStatusBar()
        {
            List<LogEntry> all = UTLog.GetAllLogs().ToList();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Total: " + all.Count, EditorStyles.miniLabel);
            GUILayout.Space(8);
            GUILayout.Label("Selected: " + selectedIds.Count, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.toolbarButton)) CopySelected();
            if (GUILayout.Button("Delete", EditorStyles.toolbarButton)) DeleteSelected();
            EditorGUILayout.EndHorizontal();
        }

        private void HandleKeyboard()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            bool command = evt.control || evt.command;
            if (command && evt.keyCode == KeyCode.A)
            {
                selectedIds.Clear();
                foreach (LogEntry entry in GetFilteredLogs()) selectedIds.Add(entry.Id);
                evt.Use();
                Repaint();
            }
            else if (command && evt.keyCode == KeyCode.C)
            {
                CopySelected();
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                DeleteSelected();
                evt.Use();
            }
        }

        private void CopySelected()
        {
            List<LogEntry> selected = UTLog.GetAllLogs().Where(x => selectedIds.Contains(x.Id)).ToList();
            if (selected.Count == 0) return;
            EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine,
                selected.Select(x => "[" + x.Time + "] [" + x.Type + "] [" + x.Tag + "] " + x.Message + Environment.NewLine + x.StackTrace).ToArray());
        }

        private void DeleteSelected()
        {
            if (selectedIds.Count == 0) return;
            UTLog.RemoveLogs(selectedIds);
            selectedIds.Clear();
            Repaint();
        }

        private void ShowContextMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Selected"), false, CopySelected);
            menu.AddItem(new GUIContent("Delete Selected"), false, DeleteSelected);
            menu.AddItem(new GUIContent("Select All Visible"), false, () =>
            {
                foreach (LogEntry entry in GetFilteredLogs()) selectedIds.Add(entry.Id);
                Repaint();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Clear All Logs"), false, UTLog.Clear);
            menu.ShowAsContext();
        }


        private void ShowToolsMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Export/Save all logs as text..."), false, ExportLogs);
            menu.AddItem(new GUIContent("Import/Import text log..."), false, ImportLogs);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Settings/Max entries/1,000"), UTLogSettings.MaxLogEntries == 1000, () => UTLogSettings.MaxLogEntries = 1000);
            menu.AddItem(new GUIContent("Settings/Max entries/10,000"), UTLogSettings.MaxLogEntries == 10000, () => UTLogSettings.MaxLogEntries = 10000);
            menu.AddItem(new GUIContent("Settings/Max entries/50,000"), UTLogSettings.MaxLogEntries == 50000, () => UTLogSettings.MaxLogEntries = 50000);
            menu.AddItem(new GUIContent("Settings/Max entries/Unlimited"), UTLogSettings.MaxLogEntries <= 0, () => UTLogSettings.MaxLogEntries = 0);
            menu.ShowAsContext();
        }

        private void ExportLogs()
        {
            string path = EditorUtility.SaveFilePanel("Export Uni7 Logs", "", "Uni7Logs.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;

            StringBuilder builder = new StringBuilder();
            foreach (LogEntry entry in UTLog.GetAllLogs())
            {
                builder.Append('[').Append(entry.Time).Append("] [").Append(entry.Type)
                    .Append("] [").Append(entry.Tag).Append("] ").AppendLine(entry.Message);
                if (!string.IsNullOrEmpty(entry.StackTrace))
                    builder.AppendLine(entry.StackTrace);
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }

        private void ImportLogs()
        {
            string path = EditorUtility.OpenFilePanel("Import Uni7 Text Log", "", "txt");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        UTLog.LogTagged("Imported", line);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OpenScriptAtLine(LogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FilePath) || entry.LineNumber <= 0)
                return;

            string path = entry.FilePath.Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");
            if (Path.IsPathRooted(path) && path.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                path = "Assets" + path.Substring(dataPath.Length);

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null)
                AssetDatabase.OpenAsset(asset, entry.LineNumber);
            else
                Debug.LogWarning("Uni7 Diagnostics: source file could not be resolved: " + entry.FilePath);
        }

        private int Count(LogType type)
        {
            return UTLog.GetAllLogs().Count(x => x.Type == type);
        }
    }
}
