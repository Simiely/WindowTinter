using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 跟踪目标窗口的位置和可见性，通过 WinEvent + 250ms 兜底轮询驱动。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

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
            if (!Native.GetWindowRect(TargetHandle, out Native.RECT r))
                return; // 窗口已销毁，跳过

            bool changed = !_hasLast
                || r.Left != _lastRect.Left || r.Top != _lastRect.Top
                || r.Right != _lastRect.Right || r.Bottom != _lastRect.Bottom
                || visible != _lastVisible;

            if (!changed) return;

            (_lastRect, _lastVisible, _hasLast) = (r, visible, true);
            OnUpdate?.Invoke(r, visible);
        }

        /// <summary>前台切换专用：无视 rect/visible 变更守卫，强制触发 OnUpdate。</summary>
        public void RefreshForeground()
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle)) return;
            if (!_hasLast) { Refresh(); return; }
            OnUpdate?.Invoke(_lastRect, _lastVisible);
        }

        // ── 静态查找方法 ──────────────────────────────────────────

        private const int MIN_TARGET_SIZE = 100;

        public static bool IsAcceptableTarget(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            Native.GetWindowRect(hwnd, out Native.RECT r);
            return r.Width >= MIN_TARGET_SIZE && r.Height >= MIN_TARGET_SIZE;
        }

        /// <summary>取窗口类名，失败返回空串。</summary>
        public static string GetWindowClass(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                return Native.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        private static string NormalizeProcessName(string processName)
        {
            string proc = processName.ToLowerInvariant();
            if (proc.EndsWith(".exe")) proc = proc[..^4]; // 兼容旧配置带 .exe 后缀
            return proc;
        }

        private struct WinCandidate
        {
            public IntPtr Handle;
            public string Title;  // 小写
            public string Class;
            public int Area;
        }

        /// <summary>枚举指定进程的全部可接受顶层窗口（可见、非最小化、尺寸达标、未被占用）。</summary>
        private static List<WinCandidate> EnumerateProcessWindows(string proc, HashSet<IntPtr> excludeHandles)
        {
            var list = new List<WinCandidate>();
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                if (excludeHandles?.Contains(hwnd) == true) return true;
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                try
                {
                    if (Process.GetProcessById((int)pid).ProcessName?.ToLowerInvariant() != proc) return true;
                }
                catch { return true; } // 进程已退出

                int len = Native.GetWindowTextLength(hwnd);
                string wtitle = "";
                if (len > 0)
                {
                    var sb = new StringBuilder(len + 1);
                    Native.GetWindowText(hwnd, sb, len + 1);
                    wtitle = sb.ToString();
                }
                Native.GetWindowRect(hwnd, out Native.RECT r);
                list.Add(new WinCandidate
                {
                    Handle = hwnd,
                    Title = wtitle.ToLowerInvariant(),
                    Class = GetWindowClass(hwnd),
                    Area = r.Width * r.Height
                });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// 按标题+进程名查找目标窗口（重绑定 / 重新查找入口）。
        /// 匹配策略按优先级递减：
        ///   1) 标题完全一致 + 进程（保持原有精确行为）
        ///   2) 标题包含关键词 + 进程（兼容浏览器/编辑器运行时标题动态变化）
        ///   3) 进程 + 窗口类名（重开后标题彻底变化时也能找回；多窗口同类时取面积最大者）
        ///   4) 仅进程兜底：该进程唯一可接受窗口直接绑；多个时优先标题含关键词者，否则取面积最大者
        /// </summary>
        public static IntPtr FindByTitleAndProcess(string title, string processName, HashSet<IntPtr> excludeHandles = null, string windowClass = null)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(processName))
                return IntPtr.Zero;

            string proc = NormalizeProcessName(processName);
            if (proc.Length == 0) return IntPtr.Zero;

            var candidates = EnumerateProcessWindows(proc, excludeHandles);
            if (candidates.Count == 0) return IntPtr.Zero;

            string kw = title.ToLowerInvariant();

            // 1) 精确标题
            var exact = candidates.Find(c => c.Title.Equals(kw, StringComparison.Ordinal));
            if (exact.Handle != IntPtr.Zero) return exact.Handle;

            // 2) 标题包含
            var contains = candidates.Find(c => c.Title.Contains(kw));
            if (contains.Handle != IntPtr.Zero) return contains.Handle;

            // 3) 进程 + 窗口类名
            if (!string.IsNullOrWhiteSpace(windowClass))
            {
                var byClass = candidates.Where(c =>
                    string.Equals(c.Class, windowClass, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byClass.Count == 1) return byClass[0].Handle;
                if (byClass.Count > 1)
                {
                    var hit = byClass.Find(c => c.Title.Contains(kw));
                    if (hit.Handle != IntPtr.Zero) return hit.Handle;
                    // 同进程同类多窗口：取面积最大者（用户关注的主窗口通常最大）
                    byClass.Sort((a, b) => b.Area.CompareTo(a.Area));
                    return byClass[0].Handle;
                }
            }

            // 4) 仅进程兜底
            if (candidates.Count == 1) return candidates[0].Handle;
            var kwHit = candidates.Find(c => c.Title.Contains(kw));
            if (kwHit.Handle != IntPtr.Zero) return kwHit.Handle;
            candidates.Sort((a, b) => b.Area.CompareTo(a.Area));
            return candidates[0].Handle;
        }

        public void Dispose()
        {
            _timer.Dispose();
            OnUpdate = null;
        }
    }
}
