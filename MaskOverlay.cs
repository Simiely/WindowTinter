using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 深色蒙版层：一个无边框、置顶、分层、鼠标穿透的半透明黑色窗口，
    /// 精准盖在目标窗口之上，跟随其位置/尺寸，整体压暗。
    /// </summary>
    internal class MaskOverlay : Form
    {
        private byte _alpha = 115;

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

        /// <summary>把蒙版对齐到目标矩形（屏幕坐标）。</summary>
        public void AlignTo(Native.RECT r)
        {
            Native.SetWindowPos(
                Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
    }
}
