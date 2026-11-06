using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Uni7.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;


namespace Uni7.Diagnostics
{
    /// <summary>Uni7用ログファサード。Unity標準Debug.Logに近い簡潔なAPIを提供します。</summary>
    public static class UTLog
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<LogEntry> Entries = new List<LogEntry>();
        private static long nextId;
        private static bool initialized;
        private static bool forwardingToUnity;
        private static bool captureUnityLogs = true;

        public static event Action<LogEntry> OnLogAdded;
        public static event Action<LogEntry> OnLogUpdated;
        public static event Action OnLogsCleared;

        public static bool IsUnityLogCaptureEnabled { get { return captureUnityLogs; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeInitialize()
        {
            Initialize();
        }

        /// <summary>明示的に初期化したい場合にも呼び出せます。複数回呼んでも安全です。</summary>
        public static void Initialize()
        {
            if (initialized) return;
            Application.logMessageReceived += OnUnityLogMessageReceived;
            initialized = true;
        }

        public static LogBuilder Log(object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Log, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, null, true));
        }

        public static LogBuilder Log(object message, UnityEngine.Object context,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Log, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, context, true));
        }

        public static LogBuilder Warning(object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Warning, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, null, true));
        }

        public static LogBuilder Warning(object message, UnityEngine.Object context,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Warning, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, context, true));
        }

        public static LogBuilder Error(object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Error, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, null, true));
        }

        public static LogBuilder Error(object message, UnityEngine.Object context,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return new LogBuilder(Add(LogType.Error, FormatObject(message), "Default",
                filePath, lineNumber, Environment.StackTrace, context, true));
        }

        public static LogEntry LogTagged(string tag, object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return Add(LogType.Log, FormatObject(message), tag, filePath, lineNumber, Environment.StackTrace, null, true);
        }

        public static LogEntry WarningTagged(string tag, object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return Add(LogType.Warning, FormatObject(message), tag, filePath, lineNumber, Environment.StackTrace, null, true);
        }

        public static LogEntry ErrorTagged(string tag, object message,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            return Add(LogType.Error, FormatObject(message), tag, filePath, lineNumber, Environment.StackTrace, null, true);
        }

        public static LogEntry LogFormat(string format, params object[] args)
        {
            string message;
            try { message = string.Format(format ?? string.Empty, args ?? new object[0]); }
            catch (FormatException ex) { message = (format ?? string.Empty) + " [FormatException: " + ex.Message + "]"; }
            return Add(LogType.Log, message, "Default", string.Empty, 0, Environment.StackTrace, null, true);
        }

        public static LogEntry WarningFormat(string format, params object[] args)
        {
            return Add(LogType.Warning, SafeFormat(format, args), "Default", string.Empty, 0, Environment.StackTrace, null, true);
        }

        public static LogEntry ErrorFormat(string format, params object[] args)
        {
            return Add(LogType.Error, SafeFormat(format, args), "Default", string.Empty, 0, Environment.StackTrace, null, true);
        }

        public static LogEntry LogIf(bool condition, object message)
        {
            return condition ? Add(LogType.Log, FormatObject(message), "Default", string.Empty, 0, Environment.StackTrace, null, true) : null;
        }

        public static LogEntry WarningIf(bool condition, object message)
        {
            return condition ? Add(LogType.Warning, FormatObject(message), "Default", string.Empty, 0, Environment.StackTrace, null, true) : null;
        }

        public static LogEntry ErrorIf(bool condition, object message)
        {
            return condition ? Add(LogType.Error, FormatObject(message), "Default", string.Empty, 0, Environment.StackTrace, null, true) : null;
        }

        public static LogEntry LogException(Exception exception, UnityEngine.Object context = null)
        {
            if (exception == null) return Error("LogException received null exception").Entry;
            return Add(LogType.Error, exception.ToString(), "Exception", string.Empty, 0,
                exception.StackTrace, context, true);
        }

        /// <summary>オブジェクトの型名とToString結果を記録します。Unity Objectの全フィールド展開は行いません。</summary>
        public static LogEntry LogObject(object value, string tag = "Object")
        {
            string message = value == null
                ? "null"
                : value.GetType().FullName + Environment.NewLine + value;
            return Add(LogType.Log, message, tag, string.Empty, 0, Environment.StackTrace, value as UnityEngine.Object, true);
        }

        public static IReadOnlyList<LogEntry> GetAllLogs()
        {
            lock (SyncRoot)
                return new ReadOnlyCollection<LogEntry>(new List<LogEntry>(Entries));
        }

        public static void Clear()
        {
            lock (SyncRoot) Entries.Clear();
            Action handler = OnLogsCleared;
            if (handler != null) handler();
        }

        public static void RemoveLogs(IEnumerable<long> ids)
        {
            if (ids == null) return;
            HashSet<long> idSet = new HashSet<long>(ids);
            lock (SyncRoot) Entries.RemoveAll(entry => idSet.Contains(entry.Id));
            Action handler = OnLogsCleared;
            if (handler != null) handler();
        }

        public static void EnableUnityLogCapture()
        {
            Initialize();
            captureUnityLogs = true;
            UTLogSettings.CaptureUnityLogs = true;
        }

        public static void DisableUnityLogCapture()
        {
            captureUnityLogs = false;
            UTLogSettings.CaptureUnityLogs = false;
        }

        internal static void UpdateLogTag(LogEntry entry, string tag)
        {
            if (entry == null) return;
            entry.SetTag(tag);
            Action<LogEntry> handler = OnLogUpdated;
            if (handler != null) handler(entry);
        }

        internal static void UpdateLogContext(LogEntry entry, UnityEngine.Object context)
        {
            if (entry == null) return;
            entry.SetContext(context);
            Action<LogEntry> handler = OnLogUpdated;
            if (handler != null) handler(entry);
        }

        private static LogEntry Add(LogType type, string message, string tag, string filePath,
            int lineNumber, string stackTrace, UnityEngine.Object context, bool mirror)
        {
            LogEntry entry;
            lock (SyncRoot)
            {
                entry = new LogEntry(++nextId, message, type, tag, filePath, lineNumber, stackTrace, context);
                Entries.Add(entry);
                int max = UTLogSettings.MaxLogEntries;
                if (max > 0 && Entries.Count > max)
                    Entries.RemoveRange(0, Entries.Count - max);
            }

            Action<LogEntry> added = OnLogAdded;
            if (added != null) added(entry);

            if (mirror && UTLogSettings.MirrorToUnityConsole)
                Mirror(entry);

            return entry;
        }

        private static void Mirror(LogEntry entry)
        {
            forwardingToUnity = true;
            try
            {
                string text = "[" + entry.Tag + "] " + entry.Message;
                switch (entry.Type)
                {
                    case LogType.Warning: Debug.LogWarning(text, entry.Context); break;
                    case LogType.Error: Debug.LogError(text, entry.Context); break;
                    default: Debug.Log(text, entry.Context); break;
                }
            }
            finally { forwardingToUnity = false; }
        }

        private static void OnUnityLogMessageReceived(string message, string stackTrace, UnityEngine.LogType unityType)
        {
            if (!captureUnityLogs || forwardingToUnity) return;

            LogType type;
            switch (unityType)
            {
                case UnityEngine.LogType.Warning: type = LogType.Warning; break;
                case UnityEngine.LogType.Error:
                case UnityEngine.LogType.Exception:
                case UnityEngine.LogType.Assert: type = LogType.Error; break;
                default: type = LogType.Log; break;
            }

            string path;
            int line;
            string tag;
            ParseStackTrace(stackTrace, out path, out line, out tag);
            Add(type, message, string.IsNullOrEmpty(tag) ? "Unity" : tag,
                path, line, stackTrace, null, false);
        }

        private static void ParseStackTrace(string stackTrace, out string path, out int line, out string tag)
        {
            path = string.Empty;
            line = 0;
            tag = string.Empty;
            if (string.IsNullOrEmpty(stackTrace)) return;

            Match fileMatch = Regex.Match(stackTrace, @"(.*\.cs):(\d+)");
            if (fileMatch.Success)
            {
                path = fileMatch.Groups[1].Value.Trim();
                int.TryParse(fileMatch.Groups[2].Value, out line);
            }

            string[] lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                Match classMatch = Regex.Match(lines[0], @"(?:at\s+)?([\w`.+]+)\.[\w`<>]+");
                if (classMatch.Success)
                {
                    string full = classMatch.Groups[1].Value;
                    int dot = full.LastIndexOf('.');
                    tag = dot >= 0 ? full.Substring(dot + 1) : full;
                }
            }
        }

        private static string SafeFormat(string format, object[] args)
        {
            try { return string.Format(format ?? string.Empty, args ?? new object[0]); }
            catch (FormatException ex) { return (format ?? string.Empty) + " [FormatException: " + ex.Message + "]"; }
        }

        private static string FormatObject(object value)
        {
            if (value == null) return "null";
            return value.ToString();
        }
    }
}
