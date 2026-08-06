using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WindowTinter.Views
{
    /// <summary>
    /// 窗口拾取器（WPF 版）：全屏近透明捕获层 + 十字光标 + 反转边框高亮。
    /// 铁律：内部逻辑（GetWindowAtCursor/IsIgnoredWindow/FallbackEnumerate/PatBlt 高亮/30ms 节流）原样搬自
    /// WinForms WindowPickerForm.cs，仅容器适配：Form→Window、MouseEventArgs→WPF 鼠标事件、
    /// Keys.Escape→Key.Escape、Control.MousePosition→PointToScreen(物理像素)。
    /// 左键点击窗口选定，右键取消，Esc 取消。
    /// </summary>
    public partial class WindowPickerWindow : Window
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private DateTime _lastMoveCheck = DateTime.MinValue;

        public WindowPickerWindow()
        {
            InitializeComponent();
            // 覆盖虚拟屏幕（WPF 按 DIP 定位）
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Activate();
        }

        /// <summary>
        /// 命中测试：优先用 WindowFromPoint（系统级命中测试，尊重 Z 序、子窗口与 WS_EX_TRANSPARENT 语义）。
        /// 若命中自己 / 桌面 / 任务栏等"不可交互"窗口，退回 Z 序枚举。
        /// </summary>
        private IntPtr GetWindowAtCursor()
        {
            var pt = PointToScreen(Mouse.GetPosition(this)); // 物理像素，供 Win32 使用
            var spt = new System.Drawing.Point((int)pt.X, (int)pt.Y);
            IntPtr h = Native.WindowFromPoint(spt);

            if (h == IntPtr.Zero || h == new WindowInteropHelper(this).Handle || IsIgnoredWindow(h))
                h = FallbackEnumerate(spt);

            return h != IntPtr.Zero ? Native.GetAncestor(h, Native.GA_ROOT) : IntPtr.Zero;
        }

        /// <summary>该窗口是否属于"不该被选中"的类型：自身、不可见、最小化、桌面/任务栏、透明覆盖层。</summary>
        private static bool IsIgnoredWindow(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;

            // 系统外壳/桌面窗口黑名单与自动绑定共用一处（TargetTracker），避免两处漏改
            string cls = GetClass(hwnd);
            if (TargetTracker.IsSystemShellWindow(cls))
                return true;

            int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            return (ex & Native.WS_EX_TRANSPARENT) != 0;
        }

        private static string GetClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>兜底：EnumWindows 按 Z 序从顶到底枚举，取第一个"包含光标 + 可交互"的顶层窗口。</summary>
        private IntPtr FallbackEnumerate(System.Drawing.Point pt)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (hwnd == new WindowInteropHelper(this).Handle || IsIgnoredWindow(hwnd)) return true;

                Native.GetWindowRect(hwnd, out Native.RECT r);
                if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            // 30ms 节流——避免每像素移动都 EnumWindows
            var now = DateTime.UtcNow;
            if ((now - _lastMoveCheck).TotalMilliseconds < 30) return;
            _lastMoveCheck = now;

            IntPtr h = GetWindowAtCursor();
            if (h == _lastHwnd) return;

            ClearHighlight();
            _lastHwnd = h;

            if (h != IntPtr.Zero && Native.GetWindowRect(h, out _highlight))
                DrawHighlight(h, _highlight);
        }

        private void OnMouseClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                CancelPicker();
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            ClearHighlight();
            IntPtr h = GetWindowAtCursor();
            if (h != IntPtr.Zero)
            {
                SelectedHandle = h;
                DialogResult = true;
            }
            else
                DialogResult = false;
            Close();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelPicker();
                e.Handled = true;
            }
        }

        private void CancelPicker()
        {
            ClearHighlight();
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ClearHighlight();
            base.OnClosing(e);
        }

        // ── 反转边框绘制（原样搬）──

        private void DrawHighlight(IntPtr hwnd, Native.RECT r)
        {
            IntPtr hdc = Native.GetWindowDC(hwnd);
            if (hdc == IntPtr.Zero) return;
            try
            {
                int bw = 3;
                Native.PatBlt(hdc, 0, 0, r.Width, bw, Native.DSTINVERT);
                Native.PatBlt(hdc, 0, r.Height - bw, r.Width, bw, Native.DSTINVERT);
                Native.PatBlt(hdc, 0, 0, bw, r.Height, Native.DSTINVERT);
                Native.PatBlt(hdc, r.Width - bw, 0, bw, r.Height, Native.DSTINVERT);
            }
            finally { Native.ReleaseDC(hwnd, hdc); }
        }

        private void ClearHighlight()
        {
            if (_lastHwnd != IntPtr.Zero && Native.IsWindow(_lastHwnd)
                && Native.GetWindowRect(_lastHwnd, out Native.RECT current))
                DrawHighlight(_lastHwnd, current);
            _lastHwnd = IntPtr.Zero;
        }
    }
}
