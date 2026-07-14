using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 窗口拾取器：全屏超低透明度捕获层 + 十字光标 + 反转边框高亮。
    /// 点击任意窗口即选定。极简、可靠。
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private Form _hintForm;

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            Opacity = 0.01;           // 几乎不可见，但仍接收鼠标事件
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            BringToFront();
            Activate();
            Cursor.Current = Cursors.Cross;
            BeginInvoke(new Action(ShowHint));
        }

        private void ShowHint()
        {
            _hintForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(340, 64),
                Location = new Point(
                    (Screen.PrimaryScreen.Bounds.Width - 340) / 2,
                    60),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(224, 224, 224),
                Opacity = 0.88,
                Owner = this,           // 绑定到拾取窗，确保模态对话框链中能正常接收输入
            };
            var lbl = new Label
            {
                Text = "左键点击窗口选择    右键 / Esc 取消选择",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular)
            };
            _hintForm.Controls.Add(lbl);
            _hintForm.Show();
        }

        private void HideHint()
        {
            if (_hintForm != null && !_hintForm.IsDisposed)
            {
                _hintForm.Close();
                _hintForm.Dispose();
                _hintForm = null;
            }
        }

        private IntPtr GetWindowAtCursor()
        {
            Point pt = Control.MousePosition;
            // WindowFromPoint 忽略 WS_DISABLED 窗口，用 Enabled 临时禁用自身替代 show/hide——无 DWM 动画闪烁
            Enabled = false;
            IntPtr h = Native.WindowFromPoint(pt);
            Enabled = true;

            // 取顶层窗口（排除子窗口）
            if (h != IntPtr.Zero && h != Handle)
                h = Native.GetAncestor(h, Native.GA_ROOT);

            return (h != IntPtr.Zero && h != Handle) ? h : IntPtr.Zero;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            IntPtr h = GetWindowAtCursor();
            if (h == _lastHwnd) return;

            // 清除旧高亮
            ClearHighlight();
            _lastHwnd = h;

            // 画新高亮
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
            HideHint();
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                CancelPicker();
        }

        private void CancelPicker()
        {
            ClearHighlight();
            DialogResult = DialogResult.Cancel;
            HideHint();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearHighlight();
            HideHint();
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
            if (_lastHwnd != IntPtr.Zero && Native.IsWindow(_lastHwnd))
                DrawHighlight(_lastHwnd, _highlight);
            _lastHwnd = IntPtr.Zero;
        }
    }
}
