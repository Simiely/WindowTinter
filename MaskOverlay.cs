using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 深色蒙版层：无边框、置顶、分层、鼠标穿透的半透明黑色窗口。
    ///
    /// 黑屏修复：用 UpdateLayeredWindow + BLENDFUNCTION 替代 SetLayeredWindowAttributes。
    /// UpdateLayeredWindow 直接将 bitmap 作为窗口内容交给 DWM 合成器，
    /// 绕过 WinForms 绘制管线，确保 alpha 混合在所有 DWM 配置下都正确。
    /// 区域裁剪仍用 SetWindowRgn（只盖目标窗口的可见部分）。
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 75;
        private IntPtr _hrgn = IntPtr.Zero;

        public MaskOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Enabled = false;
        }

        /// <summary>在创建窗口时就声明分层+穿透+置顶，确保首帧即正确合成。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        /// <summary>不透明度（0-255）。值越大越暗。</summary>
        public byte Alpha
        {
            get => _alpha;
            set => _alpha = value;
        }

        /// <summary>
        /// 把蒙版对齐到目标矩形（屏幕坐标），并裁成可见区域 hrgn（屏幕坐标，本类接管并负责释放）。
        /// </summary>
        public void AlignTo(Native.RECT r, IntPtr hrgn)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0)
            {
                Hide();
                if (hrgn != IntPtr.Zero) Native.DeleteObject(hrgn);
                return;
            }

            // 定位并显示窗口
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            // 区域裁剪：只盖目标窗口的可见区域
            if (hrgn == IntPtr.Zero)
            {
                if (_hrgn != IntPtr.Zero) { Native.DeleteObject(_hrgn); _hrgn = IntPtr.Zero; }
                Native.SetWindowRgn(Handle, IntPtr.Zero, true);
            }
            else
            {
                Native.OffsetRgn(hrgn, -r.Left, -r.Top);
                Native.SetWindowRgn(Handle, hrgn, true);
                if (_hrgn != IntPtr.Zero) Native.DeleteObject(_hrgn);
                _hrgn = hrgn;
            }

            // 用 UpdateLayeredWindow 渲染：bitmap 填纯黑，SourceConstantAlpha 控制透明度
            RenderLayered(r.Left, r.Top, w, h);
        }

        /// <summary>创建纯黑 bitmap，通过 UpdateLayeredWindow 交给 DWM 合成。</summary>
        private void RenderLayered(int x, int y, int w, int h)
        {
            // 创建纯黑 bitmap（不需要 alpha 通道，由 SourceConstantAlpha 控制透明度）
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
            }

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
                    BlendOp = 0,                  // AC_SRC_OVER
                    BlendFlags = 0,
                    SourceConstantAlpha = _alpha, // 整窗 alpha
                    AlphaFormat = 0               // 无逐像素 alpha
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

        protected override void Dispose(bool disposing)
        {
            if (_hrgn != IntPtr.Zero) { Native.DeleteObject(_hrgn); _hrgn = IntPtr.Zero; }
            base.Dispose(disposing);
        }
    }
}
