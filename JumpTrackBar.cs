using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置的自定义 TrackBar。自带底部视觉裁剪。</summary>
    internal class JumpTrackBar : TrackBar
    {
        public JumpTrackBar()
        {
            HandleCreated += (_, _) => ClipVisual();
            Resize += (_, _) => ClipVisual();
        }

        /// <summary>裁掉 TrackBar 原生控件底部的视觉溢出（轨道背景比声明区域大）。</summary>
        private void ClipVisual()
        {
            if (Width > 0 && Height > 0)
            {
                int clipH = Math.Max(Height - 8, 12);
                this.Region?.Dispose();
                this.Region = new Region(new Rectangle(0, 0, Width, clipH));
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDOWN)
            {
                int x = unchecked((short)((int)m.LParam & 0xFFFF));
                int channelW = Width - 24;
                if (channelW > 0)
                {
                    int newVal = (x - 12) * (Maximum - Minimum) / channelW + Minimum;
                    newVal = Math.Clamp(newVal, Minimum, Maximum);
                    Value = newVal; // 先跳到鼠标位置，再让默认 WndProc 处理拖拽
                }
            }
            base.WndProc(ref m);
        }
    }
}
