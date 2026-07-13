using System;
using System.IO;

namespace WindowTinter
{
    /// <summary>
    /// 极简文件日志：lock + File.AppendAllText + 时间戳。零依赖。
    /// 路径：exe 所在目录下的 WindowTinter.debug.log
    /// 超过 1MB 自动轮转为 WindowTinter.debug.log.old。
    /// </summary>
    internal static class DebugLog
    {
        private static readonly object _lock = new object();
        private static readonly string _path =
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WindowTinter.debug.log");
        private const long MaxSize = 1 * 1024 * 1024; // 1MB

        public static void Info(string msg) => Write("INFO", msg, null);
        public static void Error(string msg, Exception ex) => Write("ERROR", msg, ex);

        private static void Write(string level, string msg, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    // 轮转
                    if (File.Exists(_path) && new FileInfo(_path).Length > MaxSize)
                    {
                        string old = _path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(_path, old);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}";
                    if (ex != null) line += $"\n  → {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace}";
                    File.AppendAllText(_path, line + "\n");
                }
            }
            catch { /* 日志本身不能崩程序 */ }
        }
    }
}
