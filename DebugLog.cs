using System;

namespace WindowTinter
{
    /// <summary>调试日志（当前禁用——Info() 为空操作）。</summary>
    internal static class DebugLog
    {
        // 如需启用日志，取消注释下方实现：
        // private static readonly object _lock = new object();
        // private static readonly string _path =
        //     Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "WindowTinter.debug.log");
        //
        // public static void Info(string msg)
        // {
        //     try
        //     {
        //         lock (_lock)
        //         {
        //             var fi = new FileInfo(_path);
        //             if (fi.Exists && fi.Length > 1_000_000)
        //             {
        //                 var content = File.ReadAllText(_path);
        //                 File.WriteAllText(_path, content[^(content.Length / 2)..]);
        //             }
        //             File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        //         }
        //     }
        //     catch { }
        // }

        public static void Info(string msg)
        {
            // 调试日志已禁用
        }
    }
}
