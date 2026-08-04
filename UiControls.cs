using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>圆角按钮：自绘圆角背景 + 边框，支持 primary(teal 强调)/danger(退出)/hover 提亮。</summary>
    internal class RoundedButton : Button
    {
        public bool IsPrimary;
        public bool IsDanger;
        public int Radius = 6;

        private bool _hover;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UiRounded.Rect(ClientRectangle, Radius);
            Color fill;
            if (!Enabled) fill = Color.FromArgb(42, 44, 50);
            else if (IsPrimary) fill = _hover ? Color.FromArgb(62, 207, 142) : Color.FromArgb(42, 157, 110);
            else if (IsDanger && _hover) fill = Color.FromArgb(229, 83, 75);
            else fill = _hover ? Color.FromArgb(72, 78, 88) : Color.FromArgb(58, 63, 71);

            using var b = new SolidBrush(fill);
            e.Graphics.FillPath(b, path);
            using var pen = new Pen(IsPrimary ? Color.FromArgb(42, 157, 110) : Color.FromArgb(76, 82, 91));
            e.Graphics.DrawPath(pen, path);

            Color tc = IsPrimary ? Color.White : Color.FromArgb(232, 234, 240);
            if (!Enabled) tc = Color.FromArgb(110, 114, 122);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>teal 胶囊开关（继承 CheckBox，字段类型兼容 Checked 语义）。</summary>
    internal class SwitchToggle : CheckBox
    {
        public SwitchToggle()
        {
            AutoSize = false;
            Size = new Size(64, 20);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int knob = Math.Max(Height - 6, 14);
            int y = (Height - knob) / 2;
            var rc = new Rectangle(0, y, knob * 2, knob);
            using var path = UiRounded.Rect(rc, knob / 2);
            using var bg = new SolidBrush(Checked ? Color.FromArgb(42, 157, 110) : Color.FromArgb(58, 63, 71));
            e.Graphics.FillPath(bg, path);
            int off = Checked ? knob : 2;
            using var k = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(k, off, y + 2, knob - 4, knob - 4);
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(rc.Right + 6, 0, Width - rc.Right - 8, Height),
                    Color.FromArgb(180, 186, 196), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>圆角路径辅助。</summary>
    internal static class UiRounded
    {
        public static GraphicsPath Rect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(Math.Max(radius, 1) * 2, Math.Min(r.Width, r.Height));
            if (d < 2) { path.AddRectangle(r); path.CloseFigure(); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
