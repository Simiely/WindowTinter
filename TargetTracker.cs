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
    /// 负责：枚举可见窗口、按进程名查找、跟踪目标窗口的位置/可见性/遮挡状态。
    ///
    /// 关键改进：
    /// 1) 计算目标窗口的「可见区域」HRGN（减去其上方的遮挡窗口），供蒙版做区域裁剪；
    ///    同时回报 occluded 标志，供反色模式在「被遮挡就整体隐藏」。
    /// 2) 轮询间隔从 100ms 降为 250ms 仅作兜底；真正的更新由 SetWinEventHook 事件驱动，
    ///    且只有在几何/遮挡状态真正变化时才向下传递，避免无谓重画（修卡顿）。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

        /// <summary>本工具自身的窗口句柄集合（蒙版/宿主/放大镜），遮挡遍历时跳过，避免误判自遮挡。</summary>
        public IntPtr[] OwnWindows { get; set; } = Array.Empty<IntPtr>();

        /// <summary>
        /// 每帧回报：目标矩形（屏幕坐标）、是否可见、可见区域 HRGN（屏幕坐标，调用方负责 DeleteObject）、是否被遮挡。
        /// </summary>
        public event Action<Native.RECT, bool, IntPtr, bool> OnUpdate;

        private readonly Timer _timer;
        private readonly Native.EnumWindowsProc _enumCallback;

        // 变更守卫缓存
        private bool _hasLast;
        private Native.RECT _lastRect;
        private bool _lastVisible;
        private bool _lastOccluded;
        private Native.RECT _lastRgnBox;

        public TargetTracker()
        {
            _enumCallback = EnumVisible;
            _timer = new Timer { Interval = 250 }; // 兜底轮询，仅防漏事件；主更新走 WinEvent
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
        }

        /// <summary>供 WinEvent 回调立即触发一次刷新（带变更判断）。</summary>
        public void RefreshNow() => Refresh();

        private void Refresh()
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle))
            {
                if (_hasLast)
                {
                    _hasLast = false;
                    OnUpdate?.Invoke(new Native.RECT(), false, IntPtr.Zero, false);
                }
                return;
            }

            bool visible = Native.IsWindowVisible(TargetHandle) && !Native.IsIconic(TargetHandle);
            Native.RECT r;
            Native.GetWindowRect(TargetHandle, out r);

            bool occluded;
            IntPtr hrgn = ComputeVisibleRegion(TargetHandle, OwnWindows, out occluded);
            Native.RECT box;
            Native.GetRgnBox(hrgn, out box);

            bool changed = !_hasLast
                || r.Left != _lastRect.Left || r.Top != _lastRect.Top
                || r.Right != _lastRect.Right || r.Bottom != _lastRect.Bottom
                || visible != _lastVisible
                || occluded != _lastOccluded
                || box.Left != _lastRgnBox.Left || box.Top != _lastRgnBox.Top
                || box.Right != _lastRgnBox.Right || box.Bottom != _lastRgnBox.Bottom;

            if (changed)
            {
                _lastRect = r; _lastVisible = visible; _lastOccluded = occluded; _lastRgnBox = box; _hasLast = true;
                OnUpdate?.Invoke(r, visible, hrgn, occluded);
            }
            else
            {
                // 无变化：区域不用了，立即释放，避免 GDI 对象泄漏
                if (hrgn != IntPtr.Zero) Native.DeleteObject(hrgn);
            }
        }

        /// <summary>
        /// 计算目标窗口的可见区域（屏幕坐标 HRGN）：目标矩形减去其上方的、可见且相交的其他窗口矩形。
        /// 返回 HRGN（调用方负责 DeleteObject）；occluded 指示是否有窗口遮挡了目标。
        /// </summary>
        public static IntPtr ComputeVisibleRegion(IntPtr target, IntPtr[] ownWindows, out bool occluded)
        {
            occluded = false;
            Native.RECT tr;
            Native.GetWindowRect(target, out tr);
            if (tr.Width <= 0 || tr.Height <= 0) { occluded = false; return IntPtr.Zero; }

            IntPtr hrgn = Native.CreateRectRgn(tr.Left, tr.Top, tr.Right, tr.Bottom);
            if (hrgn == IntPtr.Zero) return IntPtr.Zero;

            var own = new HashSet<IntPtr>(ownWindows ?? Array.Empty<IntPtr>());
            IntPtr hw = target;
            // GW_HWNDPREV 沿 Z 序向上（更靠前）遍历，覆盖在目标之上的窗口都在这一链里
            while ((hw = Native.GetWindow(hw, Native.GW_HWNDPREV)) != IntPtr.Zero)
            {
                if (own.Contains(hw)) continue;
                if (!Native.IsWindowVisible(hw) || Native.IsIconic(hw)) continue;

                Native.RECT or;
                Native.GetWindowRect(hw, out or);
                // 是否与目标矩形相交
                if (or.Left < tr.Right && or.Right > tr.Left && or.Top < tr.Bottom && or.Bottom > tr.Top)
                {
                    occluded = true;
                    IntPtr hr = Native.CreateRectRgn(or.Left, or.Top, or.Right, or.Bottom);
                    if (hr != IntPtr.Zero)
                    {
                        Native.CombineRgn(hrgn, hrgn, hr, Native.RGN_DIFF);
                        Native.DeleteObject(hr);
                    }
                }
            }
            return hrgn;
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

        /// <summary>
        /// 是否为「可作为蒙版目标」的窗口：可见、未最小化、且尺寸不小于阈值
        /// （排除百度网盘那种 10x10 占位窗体等无意义的极小窗口）。
        /// </summary>
        public const int MIN_TARGET_SIZE = 100; // 单边小于此像素数视为「占位小窗」，跳过

        public static bool IsAcceptableTarget(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            return r.Width >= MIN_TARGET_SIZE && r.Height >= MIN_TARGET_SIZE;
        }

        /// <summary>按进程名（忽略大小写）查找第一个可见窗口。传入 "foo" 或 "foo.exe" 均可。会跳过极小占位窗口。</summary>
        public static IntPtr FindByProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return IntPtr.Zero;
            // Process.ProcessName 不含 .exe 后缀，这里统一去掉以便比较
            string target = processName.ToLowerInvariant();
            if (target.EndsWith(".exe")) target = target.Substring(0, target.Length - 4);
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, lparam) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true; // 跳过极小/不可见窗口，继续枚举
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    if (p.ProcessName != null && p.ProcessName.ToLowerInvariant() == target)
                    {
                        found = hwnd;
                        return false; // 命中，停止枚举
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>按窗口标题（忽略大小写、包含匹配）查找第一个可作为目标的窗口。会跳过极小占位窗口。</summary>
        public static IntPtr FindByWindowTitle(string titleKeyword)
        {
            if (string.IsNullOrWhiteSpace(titleKeyword)) return IntPtr.Zero;
            string kw = titleKeyword.ToLowerInvariant();
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, lparam) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true; // 跳过极小/不可见窗口，继续枚举
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                if (sb.ToString().ToLowerInvariant().Contains(kw))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool EnumVisible(IntPtr hwnd, IntPtr lparam) => true;

        public void Dispose() => _timer.Dispose();
    }
}
