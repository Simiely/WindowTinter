using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 蒙版层：用 UpdateLayeredWindow + BLENDFUNCTION 将纯黑 bitmap 交给 DWM 合成。
    /// clickThrough=true  → WS_EX_TRANSPARENT 鼠标穿透（前台暗色蒙版）
    /// clickThrough=false → 无 WS_EX_TRANSPARENT 捕获点击（后台激活层）
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 75;
        private Bitmap _cachedBmp;
        private int _cachedW, _cachedH;
        private readonly bool _clickThrough;
        private Action _onClick;

        public MaskOverlay(bool clickThrough = true)
        {
            _clickThrough = clickThrough;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Enabled = !clickThrough;   // clickThrough=false 时 Enabled=true 才能收鼠标事件
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST;
                if (_clickThrough)
                    cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public byte Alpha
        {
            get => _alpha;
            set => _alpha = value;
        }

        /// <summary>仅用于 clickThrough=false 模式：点击时回调。</summary>
        public void SetClickHandler(Action handler) => _onClick = handler;

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_clickThrough) _onClick?.Invoke();
            base.OnMouseUp(e);
        }

        // 阻止获取焦点（后台激活层不需要键盘输入）
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            if (!_clickThrough && m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        public void AlignTo(Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            if (!IsHandleCreated) CreateHandle();

            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            RenderLayered(r.Left, r.Top, w, h);
        }

        private void RenderLayered(int x, int y, int w, int h)
        {
            if (_cachedBmp == null || w != _cachedW || h != _cachedH)
            {
                _cachedBmp?.Dispose();
                _cachedBmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(_cachedBmp))
                    g.Clear(Color.Black);
                _cachedW = w; _cachedH = h;
            }

            IntPtr hdcScreen = Native.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return;

            IntPtr hdcMem = Native.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); return; }

            IntPtr hBmp = _cachedBmp.GetHbitmap();
            IntPtr hOld = Native.SelectObject(hdcMem, hBmp);

            try
            {
                var ptDst = new Point(x, y);
                var ptSrc = new Point(0, 0);
                var sz = new Size(w, h);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = 0, BlendFlags = 0,
                    SourceConstantAlpha = _alpha, AlphaFormat = 0
                };

                Native.UpdateLayeredWindow(Handle, hdcScreen,
                    ref ptDst, ref sz, hdcMem, ref ptSrc,
                    0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(hdcMem, hOld);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(hdcMem);
                Native.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        public new void Hide()
        {
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_HIDEWINDOW | Native.SWP_NOACTIVATE);
        }
    }
}
