using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// Spy++ 风格窗口拾取器：小浮窗 + 拖拽准星 + GDI 反转边框高亮。
    /// 用户从准星图标拖拽到目标窗口，松手即选定。不遮挡屏幕，比全屏遮罩更优雅。
    ///
    /// 原理：
    /// 1. 显示一个小浮窗，含准星图标和提示文字
    /// 2. 鼠标按下准星区域 → SetCapture，进入拖拽状态，光标变十字
    /// 3. 拖拽时 WindowFromPoint 实时获取鼠标下方窗口
    /// 4. 用 GetWindowDC + PatBlt(DSTINVERT) 在目标窗口上画反转边框（不破坏原内容）
    /// 5. 松手 → ReleaseCapture，清除高亮，返回句柄
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;

        private bool _dragging;
        private IntPtr _lastHighlighted = IntPtr.Zero;
        private Native.RECT _lastRect;

        // 用于 GDI 反转边框绘制
        private const int R2_NOT = 6; // R2_NOT 绘制模式：反转目标像素

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "选择窗口";
            ClientSize = new Size(360, 120);
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            TopMost = true;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 准星图标（用 Label 模拟，鼠标按下即开始拖拽）
            var crosshair = new Label
            {
                Text = "⊕",
                Font = new Font("Segoe UI", 28f),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(64, 64),
                Location = new Point(16, 28),
                Cursor = Cursors.Cross,
                BackColor = Color.FromArgb(240, 240, 240)
            };
            crosshair.MouseDown += (s, ev) => StartDrag(ev);
            crosshair.MouseMove += (s, ev) => OnDragMove(ev);
            crosshair.MouseUp += (s, ev) => EndDrag(ev);
            Controls.Add(crosshair);

            // 提示文字
            var hint = new Label
            {
                Text = "按住 ⊕ 拖到目标窗口\n松手即选定\n\nEsc 取消",
                Font = new Font("Microsoft YaHei UI", 10f),
                AutoSize = false,
                Size = new Size(260, 80),
                Location = new Point(96, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(hint);
        }

        private void StartDrag(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            Cursor.Current = Cursors.Cross;
            Native.SetCapture(Handle);
        }

        private void OnDragMove(MouseEventArgs e)
        {
            if (!_dragging) return;

            Point pt = Control.MousePosition;
            IntPtr h = Native.WindowFromPoint(pt);

            // 取顶层窗口（WindowFromPoint 可能返回子窗口）
            if (h != IntPtr.Zero)
                h = Native.GetAncestor(h, Native.GA_ROOT);

            // 排除自身
            if (h == Handle || h == IntPtr.Zero)
            {
                ClearHighlight();
                return;
            }

            if (h != _lastHighlighted)
            {
                ClearHighlight();
                _lastHighlighted = h;
                if (Native.GetWindowRect(h, out _lastRect))
                    DrawHighlightBorder(h, _lastRect);
            }
        }

        private void EndDrag(MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Native.ReleaseCapture();
            ClearHighlight();

            if (_lastHighlighted != IntPtr.Zero && _lastHighlighted != Handle)
            {
                SelectedHandle = _lastHighlighted;
                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
            Close();
        }

        /// <summary>在目标窗口上画反转边框（用 DSTINVERT/PatBlt，不破坏原内容）。</summary>
        private void DrawHighlightBorder(IntPtr hwnd, Native.RECT r)
        {
            IntPtr hdc = Native.GetWindowDC(hwnd);
            if (hdc == IntPtr.Zero) return;
            try
            {
                int bw = 3;
                // 上
                Native.PatBlt(hdc, 0, 0, r.Width, bw, Native.DSTINVERT);
                // 下
                Native.PatBlt(hdc, 0, r.Height - bw, r.Width, bw, Native.DSTINVERT);
                // 左
                Native.PatBlt(hdc, 0, 0, bw, r.Height, Native.DSTINVERT);
                // 右
                Native.PatBlt(hdc, r.Width - bw, 0, bw, r.Height, Native.DSTINVERT);
            }
            finally
            {
                Native.ReleaseDC(hwnd, hdc);
            }
        }

        /// <summary>清除上一个窗口的高亮边框（再画一次反转即恢复）。</summary>
        private void ClearHighlight()
        {
            if (_lastHighlighted != IntPtr.Zero && Native.IsWindow(_lastHighlighted))
            {
                DrawHighlightBorder(_lastHighlighted, _lastRect);
            }
            _lastHighlighted = IntPtr.Zero;
        }

        // 拖拽过程中鼠标可能在控件外，需要拦截窗口级消息
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONUP = 0x0202;

            if (_dragging)
            {
                if (m.Msg == WM_MOUSEMOVE)
                {
                    OnDragMove(null);
                    return;
                }
                if (m.Msg == WM_LBUTTONUP)
                {
                    EndDrag(null);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                if (_dragging) { _dragging = false; Native.ReleaseCapture(); ClearHighlight(); }
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearHighlight(); // 确保关闭时清除高亮
            base.OnFormClosing(e);
        }
    }
}
