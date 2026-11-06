using System;
using UnityEngine;

namespace Uni7.Diagnostics
{
    /// <summary>記録されたログ1件の情報。</summary>
    [Serializable]
    public sealed class LogEntry
    {
        public long Id { get; private set; }
        public string Message { get; private set; }
        public LogType Type { get; private set; }
        public string Tag { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string Time { get { return Timestamp.ToString("HH:mm:ss.fff"); } }
        public string StackTrace { get; private set; }
        public string FilePath { get; private set; }
        public int LineNumber { get; private set; }
        public UnityEngine.Object Context { get; private set; }
        public int RepeatCount { get; internal set; }

        internal LogEntry(long id, string message, LogType type, string tag,
            string filePath, int lineNumber, string stackTrace, UnityEngine.Object context)
        {
            Id = id;
            Message = message ?? "null";
            Type = type;
            Tag = string.IsNullOrEmpty(tag) ? "Default" : tag;
            Timestamp = DateTime.Now;
            FilePath = filePath ?? string.Empty;
            LineNumber = lineNumber;
            StackTrace = stackTrace ?? string.Empty;
            Context = context;
            RepeatCount = 1;
        }

        internal void SetTag(string tag)
        {
            Tag = string.IsNullOrEmpty(tag) ? "Default" : tag;
        }

        internal void SetContext(UnityEngine.Object context)
        {
            Context = context;
        }

        public override string ToString()
        {
            return string.Format("[{0}] [{1}] {2}", Type, Tag, Message);
        }
    }
}
