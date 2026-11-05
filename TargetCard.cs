using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 目标窗口卡片（设计稿：扑克牌式圆角方块，3 列网格排列）。
    /// 结构：快照区(58px) + 状态行(状态灯+名称) + 操作行(○/● ✎ ×)。
    /// 状态：普通 / hover 高亮 / 选中蓝色描边 / 待激活虚线灰态。
    /// 卡片自管理样式，深色主题遍历时跳过（ThemeAll 对 TargetCard 不干预）。
    /// </summary>
    internal class TargetCard : Panel
    {
        public TargetInfo Info { get; }
        public bool Pending { get; }
        public Button BtnSelect { get; }
        public Button BtnRename { get; }
        public Button BtnRemove { get; }

        private readonly PictureBox _shot;
        private readonly Label _shotPlaceholder;
        private readonly Label _stateDot;
        private readonly Label _lblName;
        private readonly Button _btnRefreshShot;

        private const int Radius = 10;
        private const int ShotH = 58;
        private static readonly Color C_Shot = Color.FromArgb(27, 31, 38);
        private static readonly Color C_Card = Color.FromArgb(50, 54, 60);
        private static readonly Color C_CardHover = Color.FromArgb(58, 63, 71);
        private static readonly Color C_CardPending = Color.FromArgb(38, 38, 38);
        private static readonly Color C_CardSel = Color.FromArgb(43, 59, 82);
        private static readonly Color C_Border = Color.FromArgb(58, 63, 71);
        private static readonly Color C_BorderHover = Color.FromArgb(82, 89, 102);
        private static readonly Color C_BorderSel = Color.FromArgb(74, 144, 217);
        private static readonly Color C_Btn = Color.FromArgb(65, 70, 78);
        private static readonly Color C_Text = Color.FromArgb(232, 234, 240);
        private static readonly Color C_TextDim = Color.FromArgb(120, 120, 120);

        public bool Selected { get; private set; }

        public TargetCard(TargetInfo info, bool pending, bool selected, int width, float dpiScale,
            Action<TargetInfo> onSelect, Action<TargetInfo> onRename, Action<TargetInfo> onRemove, Action<TargetInfo> onRefreshShot)
        {
            Info = info;
            Pending = pending;
            Selected = selected;
            float s = dpiScale;

            Size = new Size(width, (int)(118 * s));
            Margin = new Padding((int)(3 * s));
            BackColor = pending ? C_CardPending : selected ? C_CardSel : C_Card;
            Cursor = Cursors.Hand;
            Padding = new Padding((int)(8 * s), (int)(8 * s), (int)(8 * s), (int)(6 * s));

            SetRoundedRegion(this, Radius);

            // 快照区：PictureBox（真实快照）+ 占位（待激活/无快照）
            _shot = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = C_Shot, Visible = false };
            _shotPlaceholder = new Label
            {
                Dock = DockStyle.Fill, Text = "⏳ 窗口未启动", TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 110, 110), Font = new Font("Microsoft YaHei UI", 8.5f),
                BackColor = C_Shot, Visible = pending
            };
            var shotHost = new Panel { Dock = DockStyle.Top, Height = (int)(ShotH * s), BackColor = C_Shot };
            shotHost.Controls.Add(_shot);
            shotHost.Controls.Add(_shotPlaceholder);
            Controls.Add(shotHost);

            // 单卡刷新快照（常驻，右上角）
            _btnRefreshShot = new Button
            {
                Text = "⟳", Size = new Size((int)(18 * s), (int)(18 * s)), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 30, 36), ForeColor = Color.FromArgb(224, 224, 224),
                Cursor = Cursors.Hand, TabStop = false
            };
            _btnRefreshShot.FlatAppearance.BorderColor = Color.FromArgb(76, 82, 91);
            _btnRefreshShot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefreshShot.Location = new Point(Width - (int)(26 * s), (int)(14 * s));
            _btnRefreshShot.Click += (_, _) => onRefreshShot?.Invoke(info);
            Controls.Add(_btnRefreshShot);
            _btnRefreshShot.BringToFront();

            // 状态灯
            _stateDot = new Label
            {
                AutoSize = false, Size = new Size((int)(10 * s), (int)(10 * s)),
                Location = new Point((int)(2 * s), (int)((ShotH + 15) * s)),
                Text = pending ? "○" : "●", TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = pending ? Color.FromArgb(110, 110, 110) : Color.FromArgb(62, 207, 142),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(_stateDot);

            // 名称（单行省略，悬停 ToolTip 显示完整标题）
            _lblName = new Label
            {
                Text = pending ? "⏳ 待激活" : info.ToString(),
                AutoSize = false, Location = new Point((int)(16 * s), (int)((ShotH + 11) * s)),
                Size = new Size(Width - (int)(30 * s), (int)(22 * s)),
                ForeColor = pending ? C_TextDim : C_Text,
                Font = new Font("Microsoft YaHei UI", 9f), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Cursor = Cursors.Hand
            };
            Controls.Add(_lblName);

            // 操作行
            int btnY = (int)((ShotH + 37) * s);
            int x = (int)(2 * s);
            BtnSelect = MakeOpBtn(pending || !selected ? "○" : "●", btnY, ref x, s, onSelect);
            BtnSelect.BackColor = selected && !pending ? Color.FromArgb(74, 144, 217) : C_Btn;
            BtnSelect.Enabled = !pending;
            BtnRename = MakeOpBtn("✎", btnY, ref x, s, onRename);
            BtnRemove = MakeOpBtn("×", btnY, ref x, s, onRemove);
        }

        /// <summary>设置快照图（内部替换并释放旧图）；null = 显示占位。</summary>
        public void SetSnapshot(Image img)
        {
            if (_shot.Image != null && _shot.Image != img) _shot.Image.Dispose();
            _shot.Image = img;
            bool has = img != null;
            _shot.Visible = has;
            _shotPlaceholder.Visible = !has;
        }

        /// <summary>更新选中态外观（由 UpdateSelectButtons 驱动）。</summary>
        public void SetSelected(bool sel)
        {
            Selected = sel;
            BackColor = Pending ? C_CardPending : sel ? C_CardSel : C_Card;
            BtnSelect.Text = sel ? "●" : "○";
            BtnSelect.BackColor = sel ? Color.FromArgb(74, 144, 217) : C_Btn;
            Invalidate();
        }

        /// <summary>hover 状态由控件树自绘边框驱动（OnPaint 画边框/圆角）。</summary>
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Invalidate(); }

        private bool Hover;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 自绘圆角边框：选中蓝 / hover 亮 / 待激活虚线 / 普通暗
            using var pen = new Pen(Pending ? C_TextDim : Selected ? C_BorderSel : Hover ? C_BorderHover : C_Border, 1f);
            if (Pending) pen.DashStyle = DashStyle.Dash;
            using var path = RoundedRectPath(ClientRectangle, Radius);
            e.Graphics.DrawPath(pen, path);
        }

        private Button MakeOpBtn(string text, int y, ref int x, float s, Action<TargetInfo> onClick)
        {
            var b = new Button
            {
                Text = text, Size = new Size((int)(22 * s), (int)(22 * s)), FlatStyle = FlatStyle.Flat,
                BackColor = C_Btn, ForeColor = C_Text, Cursor = Cursors.Hand, TabStop = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(76, 82, 91);
            b.Location = new Point(x, y);
            x += (int)(26 * s);
            b.Click += (_, _) => onClick?.Invoke(Info);
            Controls.Add(b);
            return b;
        }

        private static void SetRoundedRegion(Control c, int radius)
        {
            if (c.Width < 20 || c.Height < 20) return;
            using var path = RoundedRectPath(new Rectangle(0, 0, c.Width, c.Height), radius);
            c.Region = new Region(path);
        }

        private static GraphicsPath RoundedRectPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
