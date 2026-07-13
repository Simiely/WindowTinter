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

    /// <summary>
    /// 跟踪目标窗口的位置和可见性，通过 WinEvent + 250ms 兜底轮询驱动。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

        /// <summary>本程序自身的窗口句柄（用于 IsSignificantlyOccluded 排除）。</summary>
        public IntPtr[] OwnWindows { get; set; } = Array.Empty<IntPtr>();

        /// <summary>目标窗口状态变化时触发：RECT（屏幕坐标）、是否可见。</summary>
        public event Action<Native.RECT, bool> OnUpdate;

        private readonly Timer _timer;

        // 变更守卫
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

            bool changed = !_hasLast
                || r.Left != _lastRect.Left || r.Top != _lastRect.Top
                || r.Right != _lastRect.Right || r.Bottom != _lastRect.Bottom
                || visible != _lastVisible;

            if (changed)
            {
                (_lastRect, _lastVisible, _hasLast) = (r, visible, true);
                OnUpdate?.Invoke(r, visible);
            }
        }

        // ── 静态查找方法 ──────────────────────────────────────────

        public const int MIN_TARGET_SIZE = 100;

        public static bool IsAcceptableTarget(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            Native.GetWindowRect(hwnd, out Native.RECT r);
            return r.Width >= MIN_TARGET_SIZE && r.Height >= MIN_TARGET_SIZE;
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

        public static IntPtr FindByProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return IntPtr.Zero;
            string target = processName.ToLowerInvariant();
            if (target.EndsWith(".exe")) target = target[..^4];
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    if (p.ProcessName?.ToLowerInvariant() == target) { found = hwnd; return false; }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindByWindowTitle(string titleKeyword)
        {
            if (string.IsNullOrWhiteSpace(titleKeyword)) return IntPtr.Zero;
            string kw = titleKeyword.ToLowerInvariant();
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                if (sb.ToString().ToLowerInvariant().Contains(kw)) { found = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindByTitleAndProcess(string title, string processName)
        {
            // 双匹配：标题 + 进程名
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(processName))
            {
                string proc = processName.ToLowerInvariant();
                if (proc.EndsWith(".exe")) proc = proc[..^4];
                string kw = title.ToLowerInvariant();
                IntPtr found = IntPtr.Zero;
                Native.EnumWindows((hwnd, _) =>
                {
                    if (!IsAcceptableTarget(hwnd)) return true;
                    int len = Native.GetWindowTextLength(hwnd);
                    if (len == 0) return true;
                    var sb = new StringBuilder(len + 1);
                    Native.GetWindowText(hwnd, sb, len + 1);
                    if (!sb.ToString().ToLowerInvariant().Contains(kw)) return true;
                    Native.GetWindowThreadProcessId(hwnd, out uint pid);
                    try
                    {
                        if (Process.GetProcessById((int)pid).ProcessName?.ToLowerInvariant() == proc)
                        { found = hwnd; return false; }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
            }
            return !string.IsNullOrWhiteSpace(title) ? FindByWindowTitle(title)
                 : !string.IsNullOrWhiteSpace(processName) ? FindByProcessName(processName)
                 : IntPtr.Zero;
        }

        /// <summary>
        /// 沿 Z 序向上遍历，检查是否有可见用户窗口遮挡目标超过 25% 面积。
        /// 跳过 OwnWindows（本程序自身窗口）和无标题系统窗口。
        /// </summary>
        public static bool IsSignificantlyOccluded(IntPtr target, IntPtr[] ownWindows)
        {
            if (target == IntPtr.Zero || !Native.IsWindow(target)) return false;
            Native.GetWindowRect(target, out Native.RECT tr);
            int targetArea = tr.Width * tr.Height;
            if (targetArea <= 0) return false;

            var own = new HashSet<IntPtr>(ownWindows ?? Array.Empty<IntPtr>());
            long totalOverlap = 0;
            IntPtr hw = target;

            while ((hw = Native.GetWindow(hw, Native.GW_HWNDPREV)) != IntPtr.Zero)
            {
                if (own.Contains(hw)) continue;
                if (!Native.IsWindowVisible(hw) || Native.IsIconic(hw)) continue;
                if (Native.GetWindowTextLength(hw) == 0) continue; // 跳过系统窗口

                Native.GetWindowRect(hw, out Native.RECT or);
                int overlapW = Math.Min(tr.Right, or.Right) - Math.Max(tr.Left, or.Left);
                int overlapH = Math.Min(tr.Bottom, or.Bottom) - Math.Max(tr.Top, or.Top);
                if (overlapW > 0 && overlapH > 0)
                    totalOverlap += (long)overlapW * overlapH;
            }
            return totalOverlap > targetArea * 0.25;
        }

        public void Dispose() => _timer.Dispose();
    }
}
