using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 描述一个可见窗口。
    /// </summary>
    internal class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
        public override string ToString() => $"{Title}  [{ProcessName}]";
    }

    /// <summary>
    /// 负责：枚举可见窗口、按进程名查找、定时回报目标窗口的位置与可见性。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

        /// <summary>每帧回报：目标窗口矩形 + 是否可见（未最小化且在屏幕上）。</summary>
        public event Action<Native.RECT, bool> OnUpdate;

        private readonly Timer _timer;
        // 保留委托引用，避免被 GC 回收
        private readonly Native.EnumWindowsProc _enumCallback;

        public TargetTracker()
        {
            _enumCallback = EnumVisible;
            _timer = new Timer { Interval = 100 };
            _timer.Tick += Tick;
            _timer.Start();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle))
            {
                OnUpdate?.Invoke(new Native.RECT(), false);
                return;
            }
            bool visible = Native.IsWindowVisible(TargetHandle) && !Native.IsIconic(TargetHandle);
            Native.RECT r;
            Native.GetWindowRect(TargetHandle, out r);
            OnUpdate?.Invoke(r, visible);
        }

        /// <summary>列出所有有标题的可见窗口（用于菜单/列表选择）。</summary>
        public static List<WindowInfo> GetVisibleWindows()
        {
            var list = new List<WindowInfo>();
            Native.EnumWindows((hwnd, lparam) =>
            {
                if (!Native.IsWindowVisible(hwnd)) return true;
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                string proc = "";
                try { proc = Process.GetProcessById((int)pid)?.ProcessName ?? ""; }
                catch { /* 进程可能已退出 */ }
                list.Add(new WindowInfo { Handle = hwnd, Title = sb.ToString(), ProcessName = proc });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>按进程名（忽略大小写）查找第一个可见窗口。</summary>
        public static IntPtr FindByProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return IntPtr.Zero;
            string target = processName.ToLowerInvariant();
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, lparam) =>
            {
                if (!Native.IsWindowVisible(hwnd)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    if (p.ProcessName != null && p.ProcessName.ToLowerInvariant() == target)
                    {
                        found = hwnd;
                        return false; // 停止枚举
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool EnumVisible(IntPtr hwnd, IntPtr lparam) => true;

        public void Dispose() => _timer.Dispose();
    }
}
