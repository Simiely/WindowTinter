using System;
using System.IO;

namespace WindowTinter
{
    internal static class DebugLog
    {
        private static readonly object _lock = new object();
        private static readonly string _path =
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WindowTinter.debug.log");

        public static void Info(string msg)
        {
            try { lock (_lock) File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
            catch { }
        }
    }
}
