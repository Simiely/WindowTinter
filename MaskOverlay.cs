using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 半透明蒙版层：UpdateLayeredWindow + 纯黑位图，DWM 合成暗化。
    /// WS_EX_TRANSPARENT 保证鼠标穿透，点击不拦截。
    /// </summary>
    internal class MaskOverlay : Form
    {
        // 位图缓存：只在尺寸变化时重建
        private Bitmap _bmp;
        private int _bw, _bh;

        public MaskOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Enabled = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void ShowDark(Native.RECT r, byte alpha)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            // 尺寸未变 → 复用位图
            if (_bmp == null || w != _bw || h != _bh)
            {
                _bmp?.Dispose();
                _bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                using var g = Graphics.FromImage(_bmp);
                g.Clear(Color.Black);
                _bw = w; _bh = h;
            }

            IntPtr hdcScreen = Native.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return;
            IntPtr hdcMem = Native.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); return; }

            IntPtr hBmp = _bmp.GetHbitmap();
            IntPtr hOld = Native.SelectObject(hdcMem, hBmp);

            try
            {
                var ptDst = new Point(r.Left, r.Top);
                var ptSrc = new Point(0, 0);
                var sz = new Size(w, h);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };
                Native.UpdateLayeredWindow(Handle, hdcScreen, ref ptDst, ref sz, hdcMem, ref ptSrc, 0, ref blend, Native.ULW_ALPHA);
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
