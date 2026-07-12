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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pt = PointToScreen(e.Location);
            IntPtr h = Native.WindowFromPoint(pt);
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
            IntPtr h = Native.WindowFromPoint(pt);
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
