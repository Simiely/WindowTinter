using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 深色蒙版层：用 UpdateLayeredWindow + BLENDFUNCTION 直接将纯黑 bitmap 交给 DWM 合成。
    /// TOPMOST 确保蒙版始终在目标窗口上方。
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 75;
        private Bitmap _cachedBmp;
        private int _cachedW, _cachedH;

        public MaskOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Enabled = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        public byte Alpha
        {
            get => _alpha;
            set => _alpha = value;
        }

        public void AlignTo(Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            if (!IsHandleCreated) CreateHandle();

            if (!IsHandleCreated) CreateHandle();

            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            RenderLayered(r.Left, r.Top, w, h);
        }

        private void RenderLayered(int x, int y, int w, int h)
        {
            // 仅在尺寸变化时重建 bitmap，拖滑块时只复用
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
