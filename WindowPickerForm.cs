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
            DoubleBuffered = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            BringToFront();
            Activate();
            Cursor.Current = Cursors.Cross;
            DebugLog.Info($"Picker Shown: Bounds={Bounds}, Opacity={Opacity}, Handle=0x{Handle:X}");
        }

        private IntPtr GetWindowAtCursor()
        {
            Point pt = Control.MousePosition;
            Enabled = false;
            IntPtr h = Native.WindowFromPoint(pt);
            Enabled = true;

            if (h != IntPtr.Zero && h != Handle)
                h = Native.GetAncestor(h, Native.GA_ROOT);

            IntPtr result = (h != IntPtr.Zero && h != Handle) ? h : IntPtr.Zero;
            DebugLog.Info($"Picker GetWindowAtCursor: pt={pt}, raw=0x{h:X}, ancestor=0x{(h != IntPtr.Zero ? h : IntPtr.Zero):X}, result=0x{result:X}");
            return result;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            IntPtr h = GetWindowAtCursor();
            if (h == _lastHwnd) return;

            ClearHighlight();
            _lastHwnd = h;

            if (h != IntPtr.Zero && Native.GetWindowRect(h, out _highlight))
                DrawHighlight(h, _highlight);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            DebugLog.Info($"Picker OnMouseClick: Button={e.Button}, Location={e.Location}");
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
            DebugLog.Info($"Picker ProcessCmdKey: keyData={keyData}");
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
            if (_lastHwnd != IntPtr.Zero && Native.IsWindow(_lastHwnd))
                DrawHighlight(_lastHwnd, _highlight);
            _lastHwnd = IntPtr.Zero;
        }
    }
}
