using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace WindowTinter
{
    internal class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
        public override string ToString() => $"{Title}  [{ProcessName}]";
    }

    /// <summary>跟踪目标窗口位置/可见性，WinEvent + 250ms 兜底轮询驱动。</summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; }
        public IntPtr[] OwnWindows { get; set; } = Array.Empty<IntPtr>();
        public event Action<Native.RECT, bool> OnUpdate;

        private readonly Timer _timer;
        private bool _hasLast;
        private Native.RECT _lastRect;
        private bool _lastVisible;

        public TargetTracker()
        {
            _timer = new Timer { Interval = 250 };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }

        public void RefreshNow() => Refresh();

        private void Refresh()
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle))
            {
                if (_hasLast) { _hasLast = false; OnUpdate?.Invoke(default, false); }
                return;
            }
            bool visible = Native.IsWindowVisible(TargetHandle) && !Native.IsIconic(TargetHandle);
            Native.GetWindowRect(TargetHandle, out Native.RECT r);

            if (!_hasLast || !SameRect(r, _lastRect) || visible != _lastVisible)
            {
                _lastRect = r; _lastVisible = visible; _hasLast = true;
                OnUpdate?.Invoke(r, visible);
            }
        }

        public void Dispose() => _timer.Dispose();

        private static bool SameRect(Native.RECT a, Native.RECT b) =>
            a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

        // ── 静态查找 ──────────────────────────────────────────────

        public const int MIN_TARGET_SIZE = 100;

        public static bool IsAcceptableTarget(IntPtr hwnd)
        {
            return Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)
                && Native.GetWindowRect(hwnd, out Native.RECT r)
                && r.Width >= MIN_TARGET_SIZE && r.Height >= MIN_TARGET_SIZE;
        }

        public static List<WindowInfo> GetVisibleWindows()
        {
            var list = new List<WindowInfo>();
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                string proc = "";
                try { proc = Process.GetProcessById((int)pid)?.ProcessName ?? ""; } catch { }
                list.Add(new WindowInfo { Handle = hwnd, Title = sb.ToString(), ProcessName = proc });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static IntPtr FindByTitleAndProcess(string title, string processName)
        {
            string proc = string.IsNullOrEmpty(processName) ? null : processName.ToLowerInvariant();
            if (proc?.EndsWith(".exe") == true) proc = proc[..^4];
            string kw = string.IsNullOrEmpty(title) ? null : title.ToLowerInvariant();

            IntPtr best = IntPtr.Zero;
            bool bestIsExact = false;
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                string wt = sb.ToString().ToLowerInvariant();

                if (proc != null)
                {
                    Native.GetWindowThreadProcessId(hwnd, out uint pid);
                    try { if (Process.GetProcessById((int)pid).ProcessName?.ToLowerInvariant() != proc) return true; } catch { return true; }
                }

                bool exact = kw != null && wt == kw;
                bool contains = kw == null || wt.Contains(kw);
                if (exact) { best = hwnd; bestIsExact = true; return false; }
                if (!bestIsExact && contains && best == IntPtr.Zero) best = hwnd;
                return true;
            }, IntPtr.Zero);
            return best;
        }
    }
}
