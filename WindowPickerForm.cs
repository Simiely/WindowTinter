using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 点击拾取窗口：覆盖「整个虚拟桌面」（所有显示器）的半透明捕获层，
    /// 鼠标移动时高亮其下的窗口，单击即选定（排除自身）。
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private const string Hint = "移动鼠标高亮窗口，单击选定  ·  Esc 取消";

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            // 关键修复①：覆盖所有显示器（虚拟桌面），而不是仅主屏。
            // Maximized 的无边框窗体只盖主显示器，鼠标移到副屏就没有遮罩、光标不变。
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            // 暗色半透明遮罩：既是「拾取中」的视觉提示，又照常接收鼠标事件
            // （不能用 TransparencyKey —— 会让整个表单对点击穿透，永远选不中）。
            Opacity = 0.30;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 关键修复②：强制置前并激活，确保遮罩压在所有 topmost 窗口之上、
            // 能第一时间收到鼠标/键盘（否则被百度网盘等 topmost 窗口盖住）。
            TopMost = true;
            BringToFront();
            Activate();
            Native.SetForegroundWindow(Handle);
            Focus();
            Cursor = Cursors.Cross;
        }

        // 关键修复③：分层窗口（Opacity）下 WinForms 的 Cursor 属性偶发不生效，
        // 这里拦 WM_SETCURSOR 强制十字光标兜底。
        protected override void WndProc(ref Message m)
        {
            const int WM_SETCURSOR = 0x0020;
            if (m.Msg == WM_SETCURSOR)
            {
                Cursor.Current = Cursors.Cross;
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// 查找鼠标位置下的目标窗口。先临时隐藏自身，否则 WindowFromPoint
        /// 永远返回这个全屏 TopMost 拾取层而非下面的窗口，再恢复置顶。
        /// </summary>
        private IntPtr WindowAtPoint(Point ptScreen)
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
            IntPtr h = Native.WindowFromPoint(ptScreen);
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            // WindowFromPoint 可能返回子窗口（按钮/面板等），取其顶层窗口
            if (h != IntPtr.Zero)
                h = Native.GetAncestor(h, Native.GA_ROOT);

            return h;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 用屏幕坐标（多屏虚拟桌面下 e.Location 会带负偏移，直接取 MousePosition 更稳）
            IntPtr h = WindowAtPoint(Control.MousePosition);
            if (h != IntPtr.Zero && h != Handle)
            {
                Native.RECT r;
                if (Native.GetWindowRect(h, out r)) _highlight = r;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // 高亮框：屏幕坐标 → 客户区坐标（兼容虚拟桌面负偏移）
            if (_highlight.Width > 0)
            {
                var tl = PointToClient(new Point(_highlight.Left, _highlight.Top));
                var r = new Rectangle(tl.X, tl.Y, _highlight.Width, _highlight.Height);
                using var pen = new Pen(Color.Red, 3);
                e.Graphics.DrawRectangle(pen, r);
            }

            // 顶部操作提示（画在主显示器区域内，便于看到）
            using var font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            var size = e.Graphics.MeasureString(Hint, font);
            var primary = Screen.PrimaryScreen.Bounds;
            // primary 是屏幕坐标，换算到客户区
            var pTopLeft = PointToClient(new Point(primary.Left, primary.Top));
            float x = pTopLeft.X + (primary.Width - size.Width) / 2f;
            float y = pTopLeft.Y + 60f;
            using var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            e.Graphics.FillRectangle(bg, x - 16, y - 8, size.Width + 32, size.Height + 16);
            e.Graphics.DrawString(Hint, font, Brushes.White, x, y);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            IntPtr h = WindowAtPoint(Control.MousePosition);
            if (h != IntPtr.Zero && h != Handle) SelectedHandle = h;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
