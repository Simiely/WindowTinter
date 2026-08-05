using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace WindowTinter
{
    /// <summary>
    /// 跟踪目标窗口的位置和可见性，通过 WinEvent + 500ms 兜底轮询驱动。
    /// 原型 B：每次轮询都触发 OnUpdate（不做变更守卫）——让下游有机会做 Z 序不变式校验与重插。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

        /// <summary>目标窗口状态（潜在）变化时触发：RECT（屏幕坐标）、是否可见。下游自行做差异判断。</summary>
        public event Action<Native.RECT, bool> OnUpdate;

        private readonly DispatcherTimer _timer;

        // 最近一次已知状态（供 RefreshForeground 复用）
        private bool _hasLast;
        private Native.RECT _lastRect;
        private bool _lastVisible;

        public TargetTracker()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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

            // 原型 B：不再用 changed 守卫短路——每次轮询都发 OnUpdate，
            // 由 ApplyEntryEffect / AlignBehind 内部做 alpha / 几何 / Z 序的差异与校验。
            (_lastRect, _lastVisible, _hasLast) = (r, visible, true);
            OnUpdate?.Invoke(r, visible);
        }

        /// <summary>前台切换专用：强制触发 OnUpdate（复用最近已知状态）。</summary>
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

        /// <summary>
        /// 系统外壳/桌面专用窗口类黑名单——这些窗口绝不能被当作目标。
        /// 一旦绑定（设透明 + 垫黑底），会导致桌面被压成黑屏：Progman 是桌面（Program Manager），
        /// WorkerW 是壁纸层，Shell_TrayWnd 是任务栏。开机自启时 explorer 进程里往往只有这些
        /// 窗口，此前"候选唯一/面积最大"降级绑定会命中它们，表现为开机黑屏、退出程序才恢复。
        /// </summary>
        private static readonly HashSet<string> SystemShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",                     // 桌面（Program Manager）
            "WorkerW",                     // 壁纸层
            "SHELLDLL_DefView",            // 桌面图标视图（防御性，通常为 Progman 子窗口）
            "Shell_TrayWnd",               // 任务栏
            "Shell_SecondaryTrayWnd",      // 多显示器任务栏
            "DV2ControlHost",              // 开始菜单
            "Windows.UI.Core.CoreWindow",  // 系统 UWP 壳窗口
            "MultitaskingViewFrame",       // 任务视图
            "XamlExplorerHostIslandWindow",// Win11 资源管理器宿主
            "Shell_InputSwitch",           // 输入法切换悬浮窗
            "TaskListThumbnailWnd",        // 任务栏缩略图
        };

        private static bool IsSystemShellWindow(string windowClass)
            => windowClass.Length > 0 && SystemShellWindowClasses.Contains(windowClass);

        /// <summary>枚举指定进程的全部可接受顶层窗口（可见、非最小化、尺寸达标、非系统外壳窗口、未被占用）。</summary>
        private static List<WinCandidate> EnumerateProcessWindows(string proc, HashSet<IntPtr> excludeHandles)
        {
            var list = new List<WinCandidate>();
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                // 黑屏防护：桌面/壁纸/任务栏等系统外壳窗口绝不参与匹配
                if (IsSystemShellWindow(GetWindowClass(hwnd))) return true;
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
        /// 匹配策略（title 为空时自动跳过标题条件，无标题窗口也能按 进程+类名/仅进程 绑回）：
        ///   1) 窗口类名收窄候选池（配置了类名且能在该进程匹配到时，只在同类窗口里选）
        ///   2) 标题完全一致（title 非空时）
        ///   3) 标题包含关键词（title 非空时）
        ///   4) 候选池唯一窗口直接绑
        ///   5) 面积最大者（同进程多窗口启发式）
        /// </summary>
        public static IntPtr FindByTitleAndProcess(string title, string processName, HashSet<IntPtr> excludeHandles = null, string windowClass = null)
            => FindByTitleAndProcessInternal(title, processName, excludeHandles, windowClass).Handle;

        /// <summary>公开的匹配详情（句柄 + 匹配依据），供绑定流程记录日志、排查绑定结果。</summary>
        public static (IntPtr Handle, string MatchKind) FindMatch(
            string title, string processName, HashSet<IntPtr> excludeHandles, string windowClass)
            => FindByTitleAndProcessInternal(title, processName, excludeHandles, windowClass);

        /// <summary>内部实现：附带匹配依据（供日志排查绑到了哪个窗口、依据什么）。</summary>
        private static (IntPtr Handle, string MatchKind) FindByTitleAndProcessInternal(
            string title, string processName, HashSet<IntPtr> excludeHandles, string windowClass)
        {
            string proc = NormalizeProcessName(processName);
            if (proc.Length == 0) return (IntPtr.Zero, "no-process");

            var candidates = EnumerateProcessWindows(proc, excludeHandles);
            if (candidates.Count == 0) return (IntPtr.Zero, "no-window");

            // 类名收窄候选池：配置了类名且进程内有同类窗口时，只在同类里选，降低同进程多窗口绑错概率。
            // 关键：配置了类名但进程内无同类窗口时，直接判定未找到，绝不降级到"唯一窗口/面积最大"——
            // 否则开机时（如 explorer 只有桌面/壁纸窗口）会误绑系统外壳窗口导致黑屏。
            var pool = candidates;
            if (!string.IsNullOrWhiteSpace(windowClass))
            {
                var byClass = candidates.Where(c =>
                    string.Equals(c.Class, windowClass, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byClass.Count > 0) pool = byClass;
                else return (IntPtr.Zero, "no-class-window");
            }

            string kw = title?.Trim().ToLowerInvariant() ?? "";
            bool hasTitle = kw.Length > 0;

            if (hasTitle)
            {
                var exact = pool.Find(c => c.Title.Equals(kw, StringComparison.Ordinal));
                if (exact.Handle != IntPtr.Zero) return (exact.Handle, "exact-title");

                var contains = pool.Find(c => c.Title.Contains(kw));
                if (contains.Handle != IntPtr.Zero) return (contains.Handle, "contains-title");
            }

            if (pool.Count == 1) return (pool[0].Handle, "unique-window");

            var sorted = new List<WinCandidate>(pool);
            sorted.Sort((a, b) => b.Area.CompareTo(a.Area));
            return (sorted[0].Handle, "largest-area");
        }

        public void Dispose()
        {
            _timer.Stop();
            OnUpdate = null;
        }
    }
}


