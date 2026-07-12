using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 深色蒙版层：一个无边框、置顶、分层、鼠标穿透的半透明黑色窗口，
    /// 精准盖在目标窗口的「可见区域」之上，跟随其位置/尺寸，整体压暗。
    ///
    /// 改进：用 SetWindowRgn 把蒙版裁成目标窗口未被遮挡的可见区域，
    /// 因此盖在百度网盘上面的其他窗口不会被压暗。蒙版接管传入 HRGN 的所有权。
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 115;
        private IntPtr _hrgn = IntPtr.Zero; // 当前生效的区域（本类负责释放）

        public MaskOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TopMost = true;
            Enabled = false; // 不接收输入
        }

        /// <summary>不透明度（0-255）。值越大越暗。</summary>
        public byte Alpha
        {
            get => _alpha;
            set
            {
                _alpha = value;
                if (IsHandleCreated)
                    Native.SetLayeredWindowAttributes(Handle, 0, _alpha, Native.LWA_ALPHA);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 加 WS_EX_LAYERED（分层/半透明）+ WS_EX_TRANSPARENT（鼠标穿透到下层窗口）
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            Native.SetLayeredWindowAttributes(Handle, 0, _alpha, Native.LWA_ALPHA);
        }

        /// <summary>
        /// 把蒙版对齐到目标矩形（屏幕坐标），并裁成可见区域 hrgn（屏幕坐标，本类接管并负责释放）。
        /// </summary>
        public void AlignTo(Native.RECT r, IntPtr hrgn)
        {
            Native.SetWindowPos(
                Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            if (hrgn == IntPtr.Zero)
            {
                // 无区域：恢复整窗（无裁剪），并释放旧区域
                if (_hrgn != IntPtr.Zero) { Native.DeleteObject(_hrgn); _hrgn = IntPtr.Zero; }
                Native.SetWindowRgn(Handle, IntPtr.Zero, true);
                return;
            }

            // 区域是屏幕坐标，SetWindowRgn 需要相对窗口左上角的坐标
            Native.OffsetRgn(hrgn, -r.Left, -r.Top);
            Native.SetWindowRgn(Handle, hrgn, true);

            // 接管所有权：释放上一次的旧区域
            if (_hrgn != IntPtr.Zero) Native.DeleteObject(_hrgn);
            _hrgn = hrgn;
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
