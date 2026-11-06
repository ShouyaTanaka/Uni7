using UnityEngine;

namespace Uni7.Diagnostics
{
    /// <summary>
    /// 旧チェーンAPI互換用。ログ自体は生成時に登録され、Tag変更は更新イベントで通知されます。
    /// 新規コードではUTLog.LogTaggedを推奨します。
    /// </summary>
    public sealed class LogBuilder
    {
        private readonly LogEntry entry;

        internal LogBuilder(LogEntry entry)
        {
            this.entry = entry;
        }

        public LogBuilder Tag(string tag)
        {
            UTLog.UpdateLogTag(entry, tag);
            return this;
        }

        public LogBuilder Context(UnityEngine.Object context)
        {
            UTLog.UpdateLogContext(entry, context);
            return this;
        }

        public LogEntry Entry { get { return entry; } }
    }
}
