using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 窗口拾取器：全屏透明度0.01捕获层 + 十字光标 + 反转边框高亮。
    /// 左键点击窗口选定，右键取消，Esc取消。
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private DateTime _lastMoveCheck = DateTime.MinValue;

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            Opacity = 0.01;
            Cursor = Cursors.Cross;
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            BringToFront();
            Activate();
            Cursor.Current = Cursors.Cross;
        }

        /// <summary>
        /// 命中测试：优先用 WindowFromPoint（系统级命中测试，尊重 Z 序、子窗口与 WS_EX_TRANSPARENT 语义）。
        /// 若命中自己 / 桌面 / 任务栏等"不可交互"窗口，退回 Z 序枚举，取第一个包含光标且可交互的顶层窗口。
        /// </summary>
        private IntPtr GetWindowAtCursor()
        {
            Point pt = Control.MousePosition;
            IntPtr h = Native.WindowFromPoint(pt);

            // 命中自己的全屏覆盖层 / 桌面 / 任务栏 / 透明覆盖窗 → 用枚举后备
            if (h == IntPtr.Zero || h == Handle || IsIgnoredWindow(h))
                h = FallbackEnumerate(pt);

            // 统一取根窗口（子窗口命中 → 归属其顶层所有者）
            return h != IntPtr.Zero ? Native.GetAncestor(h, Native.GA_ROOT) : IntPtr.Zero;
        }

        /// <summary>该窗口是否属于"不该被选中"的类型：自身、不可见、最小化、桌面/任务栏、透明覆盖层。</summary>
        private static bool IsIgnoredWindow(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;

            // 桌面与任务栏（含 Win11 多屏任务栏）
            string cls = GetClass(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            // 鼠标穿透 / 透明覆盖层（输入法、翻译、录屏等常驻的隐形窗口）
            int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            return (ex & Native.WS_EX_TRANSPARENT) != 0;
        }

        private static string GetClass(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// 兜底：EnumWindows 按 Z 序从顶到底枚举，取第一个"包含光标 + 可交互"的顶层窗口。
        /// 仅在 WindowFromPoint 结果不可用时调用，故省去 30ms 节流判断。
        /// </summary>
        private IntPtr FallbackEnumerate(Point pt)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (hwnd == Handle || IsIgnoredWindow(hwnd)) return true;

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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
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

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                CancelPicker();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            ClearHighlight();
            IntPtr h = GetWindowAtCursor();
            if (h != IntPtr.Zero)
            {
                SelectedHandle = h;
                DialogResult = DialogResult.OK;
            }
            else
                DialogResult = DialogResult.Cancel;
            Close();
        }

        // ProcessCmdKey 在 ProcessDialogKey 之前——后者吞掉 Esc 等导航键
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CancelPicker();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CancelPicker()
        {
            ClearHighlight();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearHighlight();
            base.OnFormClosing(e);
        }

        // ── 反转边框绘制 ──

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
