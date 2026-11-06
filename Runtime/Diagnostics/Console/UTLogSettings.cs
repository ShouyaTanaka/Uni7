namespace Uni7.Diagnostics
{
    /// <summary>ログ収集に関する実行時設定。</summary>
    public static class UTLogSettings
    {
        /// <summary>保持する最大ログ数。0以下は無制限。</summary>
        public static int MaxLogEntries { get; set; } = 10000;

        /// <summary>UTLogからUnity標準Consoleへも出力するか。</summary>
        public static bool MirrorToUnityConsole { get; set; } = true;

        /// <summary>Unity標準ログの自動キャプチャを有効にするか。</summary>
        public static bool CaptureUnityLogs { get; internal set; } = true;
    }
}
