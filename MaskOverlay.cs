using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 深色蒙版层：用 UpdateLayeredWindow + BLENDFUNCTION 直接将纯黑 bitmap 交给 DWM 合成。
    /// 覆盖目标窗口整个矩形区域，不做区域裁剪——简单可靠，绝不产生透明洞。
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 75;

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

        /// <summary>把蒙版覆盖到目标矩形（屏幕坐标）。</summary>
        public void AlignTo(Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            // 先定位并显示窗口，再 UpdateLayeredWindow 渲染内容
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            RenderLayered(r.Left, r.Top, w, h);
        }

        private void RenderLayered(int x, int y, int w, int h)
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.Black);

            IntPtr hdcScreen = Native.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return;

            IntPtr hdcMem = Native.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); return; }

            IntPtr hBmp = bmp.GetHbitmap();
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
