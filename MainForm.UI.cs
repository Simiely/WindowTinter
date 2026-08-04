using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    internal partial class MainForm : Form
    {
        // ════════════════════════════════════════════════════════════
        // 设置界面构建
        // ════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            SuspendLayout();
            _tip = new ToolTip();

            // 根容器：单列 6 行（目标卡 Percent 占满剩余空间 → 窗口拉高/DPI 变化自动增行）
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(30, 30, 30)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // 0 状态条
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 1 目标卡（占剩余）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));  // 2 效果卡
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // 3 系统卡
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // 4 操作栏
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 5 页脚

            root.Controls.Add(BuildStatusBar(), 0, 0);
            root.Controls.Add(BuildTargetCard(), 0, 1);
            root.Controls.Add(BuildEffectCard(), 0, 2);
            root.Controls.Add(BuildSystemCard(), 0, 3);
            root.Controls.Add(BuildActionBar(), 0, 4);
            root.Controls.Add(BuildFooter(), 0, 5);

            Controls.Add(root);
            ResumeLayout(true);

            // 按当前状态统一重建目标列表（活跃 + 待激活）
            RebuildTargetList();

            ApplyDarkTheme();
        }

        // ── 分区卡片容器 ──────────────────────────────────────────

        /// <summary>卡片工厂：深色圆角卡片（Region 圆角 8px + 卡头 26px + 内容区）。</summary>
        private static Panel CreateCard(Control content, string title = null)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(38, 38, 38) };
            MakeRoundedCard(card, 8);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = card.BackColor, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (title != null)
            {
                layout.RowCount = 2;
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(44, 44, 44), Margin = Padding.Empty };
                header.Controls.Add(new Label
                {
                    Text = "  " + title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(224, 224, 224)
                });
                layout.Controls.Add(header, 0, 0);
                layout.Controls.Add(content, 0, 1);
            }
            else
            {
                layout.RowCount = 1;
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.Controls.Add(content, 0, 0);
            }
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>给面板设置圆角 Region，并在尺寸变化/句柄创建后重建（可拉伸窗口下圆角保持）。</summary>
        private static void MakeRoundedCard(Panel card, int radius)
        {
            void Apply()
            {
                if (!card.IsHandleCreated || card.Width < 20 || card.Height < 20) return;
                using var path = UiRounded.Rect(new Rectangle(0, 0, card.Width, card.Height), radius);
                card.Region = new Region(path);
            }
            card.Resize += (_, _) => Apply();
            card.HandleCreated += (_, _) => Apply();
            if (card.IsHandleCreated) Apply();
        }

        /// <summary>① 状态条：状态点 + 状态文字 + ⓘ 提示 + teal 启用开关。</summary>
        private Control BuildStatusBar()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                BackColor = Color.FromArgb(38, 38, 38), Padding = new Padding(14, 0, 14, 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

            var dot = new Label
            {
                Text = "●", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(62, 207, 142)
            };
            row.Controls.Add(dot, 0, 0);

            _lblStatus = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(232, 234, 240)
            };
            row.Controls.Add(_lblStatus, 1, 0);

            var tipBtn = new RoundedButton { Text = "ⓘ", Radius = 9, Margin = new Padding(0, 11, 0, 11), TabStop = false };
            _tip.SetToolTip(tipBtn, "总开关：启用后对所有目标窗口生效压暗与垫黑效果；停用后全部窗口立即还原。");
            row.Controls.Add(tipBtn, 2, 0);

            _chkEnabled = new SwitchToggle { Text = "启用", Dock = DockStyle.Right, Margin = Padding.Empty };
            _chkEnabled.CheckedChanged += (_, _) => ToggleEnabled();
            row.Controls.Add(_chkEnabled, 3, 0);

            return CreateCard(row);
        }

        /// <summary>② 目标窗口卡：卡头（标题+徽标+刷新快照+添加+重新查找）+ 3 列卡片网格 + 增行提示。</summary>
        private Control BuildTargetCard()
        {
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(44, 44, 44), Height = 26 };
            var hLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = header.BackColor, Margin = Padding.Empty };
            hLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            hLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            hLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            hLayout.Controls.Add(new Label
            {
                Text = " 目标窗口", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(224, 224, 224)
            }, 0, 0);
            _lblTargetBadge = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8f), ForeColor = Color.FromArgb(62, 207, 142)
            };
            hLayout.Controls.Add(_lblTargetBadge, 1, 0);

            var btnRefresh = new RoundedButton { Text = "⟳ 刷新快照", Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 3) };
            btnRefresh.Click += (_, _) => RefreshAllSnapshots();
            hLayout.Controls.Add(btnRefresh, 2, 0);
            var btnAdd = new RoundedButton { Text = "+ 添加窗口", IsPrimary = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            btnAdd.Click += (_, _) => PickWindow();
            hLayout.Controls.Add(btnAdd, 3, 0);
            _btnRefind = new RoundedButton { Text = "↻ 重新查找", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            _btnRefind.Click += (_, _) => RefindAllWindows();
            hLayout.Controls.Add(_btnRefind, 4, 0);
            header.Controls.Add(hLayout);

            _pnlTargets = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                AutoScroll = true, BackColor = Color.FromArgb(32, 32, 32), Margin = Padding.Empty,
                Padding = new Padding(6)
            };
            _pnlTargets.HandleCreated += (_, _) =>
                Native.SetWindowTheme(_pnlTargets.Handle, "DarkMode_Explorer", null);

            var more = new Label
            {
                Text = "▼ 更多目标 · 窗口拉高后卡片自动增行", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 8f), ForeColor = Color.FromArgb(100, 104, 112),
                BackColor = Color.FromArgb(32, 32, 32), Height = 16
            };

            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(38, 38, 38) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = card.BackColor, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(_pnlTargets, 0, 1);
            layout.Controls.Add(more, 0, 2);
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>③ 效果控制卡：垫黑底+全局压暗 / 压暗滑块 / 全局圆角 / 圆角滑块 / 提示（容器对齐）。</summary>
        private Control BuildEffectCard()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(10, 4, 10, 4)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // 0 垫黑底 + 全局压暗
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 1 压暗滑块
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // 2 全局圆角
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 3 圆角滑块
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 4 提示

            // Row0：垫黑底（左）+ 全局统一压暗（右）
            var r0 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = layout.BackColor, Margin = Padding.Empty };
            r0.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            r0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            r0.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _chkBackdropPlate = AddCheck(r0, "垫黑底", 0, 0, FontStyle.Regular,
                _settings.BackdropBlackPlate, () => { _settings.BackdropBlackPlate = _chkBackdropPlate.Checked; _settings.Save(); foreach (var e in _entries) e.Tracker.RefreshForeground(); });
            _tip.SetToolTip(_chkBackdropPlate, "在目标窗口正下方垫不透明纯黑，配合半透明形成压暗效果。关闭后目标只变半透明、透出后面内容。");
            r0.Controls.Add(_chkBackdropPlate, 0, 0);
            r0.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = r0.BackColor }, 1, 0);
            _chkGlobalTransparency = AddCheck(r0, "全局统一压暗", 0, 0, FontStyle.Bold,
                _settings.GlobalTransparency, ToggleGlobalTransparency);
            _tip.SetToolTip(_chkGlobalTransparency, "所有窗口共用同一压暗强度。关闭后可在目标列表点 ○ 选中窗口单独配置。");
            r0.Controls.Add(_chkGlobalTransparency, 2, 0);
            layout.Controls.Add(r0, 0, 0);

            // Row1：压暗滑块
            _tbBgAlpha = new JumpTrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 20, Value = _settings.BackgroundAlpha };
            _tbBgAlpha.ValueChanged += (_, _) => SetBgAlpha(_tbBgAlpha.Value);
            _lblBgAlpha = new Label { Text = $"{_settings.BackgroundAlpha}%", AutoSize = false };
            layout.Controls.Add(BuildSliderRow("压暗强度", _tbBgAlpha, _lblBgAlpha), 0, 1);

            // Row2：全局统一圆角
            var r2 = new Panel { Dock = DockStyle.Fill, BackColor = layout.BackColor, Height = 28, Margin = Padding.Empty };
            _chkGlobalCornerRadius = AddCheck(r2, "全局统一圆角", 0, 2, FontStyle.Bold,
                _settings.GlobalCornerRadius, ToggleGlobalCornerRadius);
            _tip.SetToolTip(_chkGlobalCornerRadius, "所有窗口共用同一圆角。关闭后可在目标列表点 ○ 选中窗口单独配置。");
            layout.Controls.Add(r2, 0, 2);

            // Row3：圆角滑块
            _tbCornerRadius = new JumpTrackBar { Minimum = 0, Maximum = 20, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Value = _settings.CornerRadius };
            _tbCornerRadius.ValueChanged += (_, _) => SetCornerRadius(_tbCornerRadius.Value);
            _lblCornerRadius = new Label { Text = _settings.CornerRadius == 0 ? "关" : $"{_settings.CornerRadius}px", AutoSize = false };
            layout.Controls.Add(BuildSliderRow("圆角半径", _tbCornerRadius, _lblCornerRadius), 0, 3);

            // Row4：提示
            layout.Controls.Add(new Label
            {
                Text = "关闭「全局统一」后，点 ○ 选中窗口单独调整",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(150, 150, 160),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            }, 0, 4);

            return CreateCard(layout, "效果控制");
        }

        /// <summary>滑块行：标签(固定宽) | 滑块(Fill) | 数值(固定宽)——任意宽度不溢出。</summary>
        private static TableLayoutPanel BuildSliderRow(string label, TrackBar tb, Label lblValue)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.FromArgb(40, 40, 40), Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false }, 0, 0);
            tb.Dock = DockStyle.Fill; tb.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(tb, 1, 0);
            lblValue.Dock = DockStyle.Fill; lblValue.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(lblValue, 2, 0);
            return row;
        }

        /// <summary>④ 系统选项卡：开机自启 + 关闭时最小化到托盘（一行）+ ⓘ。</summary>
        private Control BuildSystemCard()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(14, 8, 14, 8)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _chkStartup = AddCheck(bar, "开机自启", 0, 0, FontStyle.Regular, _settings.StartWithWindows,
                () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); _settings.Save(); });
            _tip.SetToolTip(_chkStartup, "登录 Windows 时自动启动本程序（写入注册表 Run 项）。");
            bar.Controls.Add(_chkStartup, 0, 0);

            _chkMinimizeTray = AddCheck(bar, "关闭时最小化到托盘", 0, 0, FontStyle.Regular,
                _settings.MinimizeToTray, () => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; _settings.Save(); });
            _tip.SetToolTip(_chkMinimizeTray, "勾选后点关闭按钮最小化到托盘常驻；不勾选则直接退出程序。");
            bar.Controls.Add(_chkMinimizeTray, 1, 0);

            bar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = bar.BackColor }, 2, 0);

            return CreateCard(bar);
        }

        /// <summary>⑤ 操作栏：次要靠左、保存强调靠右、退出 hover 变红。</summary>
        private Control BuildActionBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1,
                BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(10, 8, 10, 8)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            var b1 = new RoundedButton { Text = "📂 配置文件夹", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            b1.Click += (_, _) => OpenConfigFolder();
            bar.Controls.Add(b1, 0, 0);
            var b2 = new RoundedButton { Text = "ℹ 关于", Dock = DockStyle.Fill, Margin = Padding.Empty };
            b2.Click += (_, _) => ShowAbout();
            bar.Controls.Add(b2, 1, 0);
            bar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = bar.BackColor }, 2, 0);
            var b4 = new RoundedButton { Text = "💾 保存配置", IsPrimary = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            b4.Click += (_, _) => SaveSettings();
            bar.Controls.Add(b4, 3, 0);
            var b5 = new RoundedButton { Text = "🚪 退出", IsDanger = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            b5.Click += (_, _) => { _reallyQuit = true; Close(); };
            bar.Controls.Add(b5, 4, 0);

            return CreateCard(bar);
        }

        /// <summary>⑥ 页脚：版本链接。</summary>
        private Control BuildFooter()
        {
            var link = new LinkLabel
            {
                Text = "20260720 · 世界的风吹向你 · 开源软件  |  GitHub →",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                LinkColor = Color.FromArgb(160, 160, 170),
                ActiveLinkColor = Color.FromArgb(62, 207, 142)
            };
            link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Simiely/WindowTinter") { UseShellExecute = true });
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), Padding = new Padding(12, 4, 0, 0) };
            p.Controls.Add(link);
            return p;
        }

        // ── 控件辅助方法 ──────────────────────────────────────────

        private static CheckBox AddCheck(Control parent, string text, int x, int y, FontStyle style, bool initial, Action onChange)
        {
            var chk = new CheckBox
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Checked = initial
            };
            if (style != FontStyle.Regular)
                chk.Font = new Font("Microsoft YaHei UI", 9.5f, style);
            chk.CheckedChanged += (_, _) => onChange();
            parent.Controls.Add(chk);
            return chk;
        }

        // ════════════════════════════════════════════════════════════
        // 深色主题
        // ════════════════════════════════════════════════════════════

        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.FromArgb(224, 224, 224);
            var panelBg = Color.FromArgb(40, 40, 40);

            BackColor = bg;
            ForeColor = fg;
            if (IsHandleCreated)
            {
                int dark = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            }
            ThemeAll(this, bg, fg, panelBg);
        }

        private static void ThemeAll(Control parent, Color bg, Color fg, Color panelBg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TargetCard or RoundedButton or SwitchToggle) continue; // 自绘控件自管理样式，主题不干预
                if (c is GroupBox or Panel or FlowLayoutPanel) { c.BackColor = panelBg; c.ForeColor = fg; }
                else if (c is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = fg; btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (c is CheckBox or RadioButton) { c.BackColor = bg; c.ForeColor = fg; }
                else if (c is Label lbl) { lbl.BackColor = lbl.Parent is Panel or FlowLayoutPanel ? panelBg : bg; if (lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.FromArgb(140, 140, 140); else lbl.ForeColor = fg; }
                else if (c is TrackBar) { c.BackColor = panelBg; }
                else { c.BackColor = bg; c.ForeColor = fg; }
                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        // ════════════════════════════════════════════════════════════
        // UI 同步
        // ════════════════════════════════════════════════════════════

        /// <summary>统一状态文案：主窗口状态栏与托盘菜单共用，避免两处重复计算漂移。</summary>
        private string GetStatusText()
        {
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            return !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 个监控中, {pending} 个待激活"
                : $"● 监控中 — {active} 个窗口";
        }

        private void UpdateUI()
        {
            _lblStatus.Text = GetStatusText();

            _chkEnabled.Checked = _settings.Enabled;
            _chkGlobalTransparency.Checked = _settings.GlobalTransparency;
            _chkGlobalCornerRadius.Checked = _settings.GlobalCornerRadius;

            // 非全局模式下，若选中目标已不在列表中则清空
            if (!_settings.GlobalTransparency && _selectedTarget != null && !_settings.Targets.Any(t => t.Equals(_selectedTarget)))
                _selectedTarget = null;

            int b = _settings.GlobalTransparency ? _settings.BackgroundAlpha : (_selectedTarget?.BackgroundAlpha ?? 0);
            if (_tbBgAlpha.Value != b) _tbBgAlpha.Value = b;
            UpdateSliderEnabled();
            _lblBgAlpha.Text = $"{_tbBgAlpha.Value}%";

            int cr = _settings.GlobalCornerRadius ? _settings.CornerRadius : (_selectedTarget?.CornerRadius ?? 0);
            if (_tbCornerRadius.Value != cr) _tbCornerRadius.Value = cr;
            UpdateCornerSliderEnabled();
            _lblCornerRadius.Text = cr == 0 ? "关" : $"{cr}px";
            UpdateSelectButtons();
            RefreshTrayMenu();
        }

        // ════════════════════════════════════════════════════════════
        // 选中目标（非全局模式）
        // ════════════════════════════════════════════════════════════

        private void SelectTarget(TargetInfo info)
        {
            if (_settings.GlobalTransparency && _settings.GlobalCornerRadius) return;
            _selectedTarget = info;
            UpdateSelectButtons();
            // 选中目标时一次性带到前台（不抢焦点），便于查看半透明/黑底效果；
            // 之后前后遮挡回归 Windows 默认逻辑，不永久置顶。
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null)
            {
                BringTargetToTop(entry.Tracker.TargetHandle);
                ApplyMaskNow(entry);
            }
            if (!_settings.GlobalTransparency)
            {
                int b = info.BackgroundAlpha;
                if (_tbBgAlpha.Value != b) _tbBgAlpha.Value = b;
                _lblBgAlpha.Text = $"{b}%";
                UpdateSliderEnabled();
            }
            if (!_settings.GlobalCornerRadius)
            {
                int cr = info.CornerRadius;
                if (_tbCornerRadius.Value != cr) _tbCornerRadius.Value = cr;
                _lblCornerRadius.Text = cr == 0 ? "关" : $"{cr}px";
                UpdateCornerSliderEnabled();
            }
        }

        private void UpdateSelectButtons()
        {
            bool global = _settings.GlobalTransparency;
            foreach (var kv in _selectButtons)
            {
                bool sel = !global && _selectedTarget != null && _selectedTarget.Equals(kv.Key);
                kv.Value.Text = sel ? "●" : "○";
                kv.Value.Enabled = !global;
                kv.Value.BackColor = sel ? Color.FromArgb(74, 144, 217) : Color.FromArgb(65, 70, 78);
            }
            // 卡片选中态（选中蓝色描边 + 背景）
            foreach (var e in _entries)
                if (e.UIPanel is TargetCard tc)
                    tc.SetSelected(!global && _selectedTarget != null && _selectedTarget.Equals(e.Info));
            foreach (var kv in _pendingPanels)
                if (kv.Value is TargetCard tcp)
                    tcp.SetSelected(!global && _selectedTarget != null && _selectedTarget.Equals(kv.Key));
        }

        /// <summary>非全局模式且未选中任何目标时，禁用手动滑块避免"空转"（此时调节无效果）。</summary>
        private void UpdateSliderEnabled()
        {
            bool enabled = _settings.GlobalTransparency || _selectedTarget != null;
            _tbBgAlpha.Enabled = enabled;
        }

        private void UpdateCornerSliderEnabled()
        {
            bool enabled = _settings.GlobalCornerRadius || _selectedTarget != null;
            _tbCornerRadius.Enabled = enabled;
        }

        // ════════════════════════════════════════════════════════════
        // 目标列表 UI 重建（活跃 + 待激活，按配置顺序）
        // ════════════════════════════════════════════════════════════

        /// <summary>按状态统一重建目标卡片网格（3 列扑克牌式，按配置顺序）。</summary>
        private void RebuildTargetList()
        {
            _pnlTargets.Controls.Clear();
            _pendingPanels.Clear();
            _selectButtons.Clear();

            if (_settings.Targets.Count == 0)
            {
                // 空状态引导
                _pnlTargets.Controls.Add(new Label
                {
                    Text = "○ 点击「+ 添加窗口」拾取要压暗的窗口",
                    AutoSize = true, ForeColor = Color.FromArgb(140, 140, 140),
                    Font = new Font("Microsoft YaHei UI", 9.5f), Margin = new Padding(24, 34, 0, 0)
                });
            }
            else
            {
                int cardW = CalcCardWidth();
                foreach (var t in _settings.Targets)
                {
                    var entry = _entries.FirstOrDefault(e => e.Info == t);
                    var card = CreateTargetCard(t, pending: entry == null, cardW);
                    if (entry != null) entry.UIPanel = card;
                    else _pendingPanels[t] = card;
                    _pnlTargets.Controls.Add(card);
                }
            }
            UpdateTargetBadge();
            UpdateSelectButtons();
        }

        /// <summary>3 列网格卡片宽度：面板可用宽 / 3，留卡间距。</summary>
        private int CalcCardWidth()
        {
            int cw = _pnlTargets.ClientSize.Width - 12; // 两侧 padding 6
            return Math.Max((cw - 12) / 3, 110);        // 3 列 + 卡间距 12
        }

        /// <summary>创建单张目标卡片（活跃或待激活）。</summary>
        private TargetCard CreateTargetCard(TargetInfo info, bool pending, int width)
        {
            bool sel = !_settings.GlobalTransparency && _selectedTarget != null && _selectedTarget.Equals(info);
            var card = new TargetCard(info, pending, sel, width, _dpiScale,
                SelectTarget, RenameTarget, RemoveTarget, RefreshTargetSnapshot);
            _selectButtons[info] = card.BtnSelect;
            return card;
        }

        /// <summary>目标卡头计数徽标：监控/待激活数量。</summary>
        private void UpdateTargetBadge()
        {
            int a = _entries.Count;
            int p = _settings.Targets.Count - a;
            _lblTargetBadge.Text = _settings.Targets.Count == 0 ? ""
                : p > 0 ? $"● {a} 监控 · {p} 待激活" : $"● {a} 监控";
        }

        /// <summary>刷新单张卡片快照（单卡 ⟳ / 自动定时 / 绑定移动即时）。</summary>
        private void RefreshTargetSnapshot(TargetInfo info)
        {
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry == null || entry.UIPanel is not TargetCard card) return;
            var shot = SnapshotService.Capture(entry.Tracker.TargetHandle, Math.Max(card.Width - 16, 90), 58);
            card.SetSnapshot(shot);
        }

        /// <summary>全量刷新所有目标卡片快照（卡头「⟳ 刷新快照」）。</summary>
        private void RefreshAllSnapshots()
        {
            foreach (var t in _settings.Targets) RefreshTargetSnapshot(t);
        }
    }
}
