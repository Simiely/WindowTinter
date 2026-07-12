using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 点击拾取窗口：全屏半透明捕获层，鼠标移动时高亮其下的窗口，
    /// 单击即选定（排除自身）。
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            Opacity = 0.3;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
        }

        /// <summary>
        /// 查找鼠标位置下的目标窗口。先临时隐藏自身，否则 WindowFromPoint
        /// 永远返回这个全屏 TopMost 拾取层而非下面的窗口。
        /// </summary>
        private IntPtr WindowAtPoint(Point pt)
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
            IntPtr h = Native.WindowFromPoint(pt);
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);

            // WindowFromPoint 可能返回子窗口（按钮/面板等），取其顶层窗口
            if (h != IntPtr.Zero)
                h = Native.GetAncestor(h, Native.GA_ROOT);

            return h;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pt = PointToScreen(e.Location);
            IntPtr h = WindowAtPoint(pt);
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
            if (_highlight.Width > 0)
            {
                var r = new Rectangle(_highlight.Left, _highlight.Top,
                    _highlight.Width, _highlight.Height);
                using var pen = new Pen(Color.Red, 3);
                e.Graphics.DrawRectangle(pen, r);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var pt = PointToScreen(e.Location);
            IntPtr h = WindowAtPoint(pt);
            if (h != IntPtr.Zero && h != Handle) SelectedHandle = h;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)27) // Esc 取消
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
