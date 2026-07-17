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
            // 可滚动布局容器：承载所有“卡片”。控件尺寸固定，仅卡片间距随窗口变化。
            _layout = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            Controls.Add(_layout);

            const int INNER = 10;   // 卡片内边距
            const int HEAD = 28;    // 卡片头部高度

            // ── 卡片：状态 ──
            var cStatus = MakeCard("状态", 56);
            _lblStatus = new Label
            {
                Location = new Point(INNER, HEAD + 8), AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(224, 224, 224)
            };
            cStatus.Controls.Add(_lblStatus);

            // ── 卡片：开关 ──
            var cToggles = MakeCard("开关", 64);
            _chkEnabled = new CheckBox
            {
                Text = "启用覆盖", Location = new Point(INNER + 4, HEAD + 8),
                AutoSize = true, Checked = _settings.Enabled,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
            };
            _chkEnabled.CheckedChanged += (_, _) => ToggleEnabled();
            _chkBackdropPlate = new CheckBox
            {
                Text = "下方垫黑色（下层遮罩）", Location = new Point(150, HEAD + 8),
                AutoSize = true, Checked = _settings.BackdropBlackPlate
            };
            _chkBackdropPlate.CheckedChanged += (_, _) =>
            {
                _settings.BackdropBlackPlate = _chkBackdropPlate.Checked; _settings.Save();
                foreach (var e in _entries) e.Tracker.RefreshForeground();
            };
            cToggles.Controls.Add(_chkEnabled);
            cToggles.Controls.Add(_chkBackdropPlate);

            // ── 卡片：目标窗口 ──
            var cTargets = MakeCard("目标窗口", 300);
            _pnlTargets = new FlowLayoutPanel
            {
                Location = new Point(INNER, HEAD + 6), Size = new Size(416, 210),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.FromArgb(32, 32, 32), ForeColor = Color.White
            };
            _pnlTargets.HandleCreated += (_, _) =>
                Native.SetWindowTheme(_pnlTargets.Handle, "DarkMode_Explorer", null);
            cTargets.Controls.Add(_pnlTargets);
            _btnRefind = AddButton(cTargets, "🔄 重新查找", 115, HEAD + 224, 95, RefindAllWindows);
            AddButton(cTargets, "+ 添加窗口", INNER, HEAD + 224, 95, PickWindow);

            // ── 卡片：窗口控制 ──
            var cCtrl = MakeCard("窗口控制", 220);
            _chkGlobalTransparency = AddCheck(cCtrl, "全局统一透明度（关闭后每个窗口单独配置）", INNER, HEAD + 8, FontStyle.Bold,
                _settings.GlobalTransparency, ToggleGlobalTransparency);
            cCtrl.Controls.Add(new Label { Text = "窗口透明度:", Location = new Point(INNER, HEAD + 44), AutoSize = true });
            _tbBgAlpha = new JumpTrackBar
            {
                Location = new Point(100, HEAD + 42), Size = new Size(260, 24),
                Minimum = 0, Maximum = 100, TickFrequency = 10,
                SmallChange = 5, LargeChange = 20,
                Value = _settings.BackgroundAlpha
            };
            _tbBgAlpha.ValueChanged += (_, _) => SetBgAlpha(_tbBgAlpha.Value);
            cCtrl.Controls.Add(_tbBgAlpha);
            _lblBgAlpha = new Label { Location = new Point(366, HEAD + 44), AutoSize = true };
            cCtrl.Controls.Add(_lblBgAlpha);

            _chkGlobalCornerRadius = AddCheck(cCtrl, "全局统一圆角（关闭后每个窗口单独配置）", INNER, HEAD + 80, FontStyle.Bold,
                _settings.GlobalCornerRadius, ToggleGlobalCornerRadius);
            cCtrl.Controls.Add(new Label { Text = "圆角半径:", Location = new Point(INNER, HEAD + 116), AutoSize = true });
            _tbCornerRadius = new JumpTrackBar
            {
                Location = new Point(100, HEAD + 114), Size = new Size(260, 24),
                Minimum = 0, Maximum = 20, TickFrequency = 5,
                SmallChange = 1, LargeChange = 5,
                Value = _settings.CornerRadius
            };
            _tbCornerRadius.ValueChanged += (_, _) => SetCornerRadius(_tbCornerRadius.Value);
            cCtrl.Controls.Add(_tbCornerRadius);
            _lblCornerRadius = new Label { Location = new Point(366, HEAD + 116), AutoSize = true,
                Text = _settings.CornerRadius == 0 ? "关" : $"{_settings.CornerRadius}px" };
            cCtrl.Controls.Add(_lblCornerRadius);
            cCtrl.Controls.Add(new Label
            {
                Text = "关闭任一全局开关后，在列表中点击 ○ 选中窗口单独配置",
                Location = new Point(INNER, HEAD + 152), AutoSize = true,
                ForeColor = Color.FromArgb(150, 150, 160), Font = new Font("Microsoft YaHei UI", 8.5f)
            });

            // ── 卡片：系统选项 ──
            var cSys = MakeCard("系统选项", 96);
            _chkStartup = AddCheck(cSys, "开机自启", INNER + 4, HEAD + 8, FontStyle.Regular, _settings.StartWithWindows,
                () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); _settings.Save(); });
            _chkMinimizeTray = AddCheck(cSys, "关闭窗口时最小化到托盘（不勾选则直接退出）", INNER + 4, HEAD + 34, FontStyle.Regular,
                _settings.MinimizeToTray, () => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; _settings.Save(); });

            // ── 卡片：操作 ──
            var cActions = MakeCard("操作", 110);
            AddButton(cActions, "📂 配置文件夹", INNER, HEAD + 8, 120, OpenConfigFolder);
            AddButton(cActions, "ℹ 关于", 134, HEAD + 8, 70, ShowAbout);
            AddButton(cActions, "💾 保存配置", 210, HEAD + 8, 110, SaveSettings);
            AddButton(cActions, "🚪 退出", 326, HEAD + 8, 70, () => { _reallyQuit = true; Close(); });
            var link = new LinkLabel
            {
                Text = "20260714 / 世界的风吹向你 / 开源软件",
                Location = new Point(INNER, HEAD + 44), AutoSize = true,
                LinkColor = Color.FromArgb(160, 160, 170),
                ActiveLinkColor = Color.FromArgb(200, 200, 220)
            };
            link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Simiely/WindowTinter") { UseShellExecute = true });
            cActions.Controls.Add(link);

            // 恢复已有条目（活跃 + 待激活）
            for (int i = 0; i < _entries.Count; i++)
                AddTargetUI(_entries[i]);
            foreach (var t in _settings.Targets)
                if (!_entries.Any(e => e.Info == t))
                    AddPendingUI(t);

            ApplyDarkTheme();
            // ThemeAll 会把 Panel 染成 panelBg，这里把容器还原为表单底色，并把目标列表还原为深色
            _layout.BackColor = Color.FromArgb(30, 30, 30);
            _pnlTargets.BackColor = Color.FromArgb(32, 32, 32);
        }

        // ── 控件辅助方法 ──────────────────────────────────────────

        /// <summary>创建一张卡片（带头部色带与边框的固定尺寸容器），并加入布局容器与卡片列表。</summary>
        private Panel MakeCard(string title, int height)
        {
            var p = new Panel
            {
                BackColor = Color.FromArgb(40, 40, 40),
                Size = new Size(CARD_W_BASE, height),
                Margin = new Padding(0)
            };
            p.Paint += (_, e) =>
            {
                using var hb = new SolidBrush(Color.FromArgb(52, 52, 60));
                e.Graphics.FillRectangle(hb, 1, 1, p.Width - 2, 26);     // 头部色带
                using var pen = new Pen(Color.FromArgb(80, 80, 92));
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); // 边框
            };
            var t = new Label
            {
                Text = title, Location = new Point(10, 6), AutoSize = true,
                ForeColor = Color.FromArgb(210, 210, 220),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            p.Controls.Add(t);
            _cards.Add(new Card { Panel = p, FixedHeight = height });
            _layout.Controls.Add(p);
            return p;
        }

        private static Label AddLabel(Control parent, int x, int y, FontStyle style, float size = 9f)
        {
            var lbl = new Label { Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", size, style) };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Button AddButton(Control parent, string text, int x, int y, int w, Action onClick)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30) };
            btn.Click += (_, _) => onClick();
            parent.Controls.Add(btn);
            return btn;
        }

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

        /// <summary>创建目标面板中的 ○/● 选中按钮。尺寸/位置使用固定基准值（面板整体不缩放）。</summary>
        private Button CreateSelectButton(int w, bool selected, bool enabled, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = selected ? "●" : "○",
                Size = new Size(30, 24),
                Location = new Point(w - 68, 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = selected ? Color.FromArgb(90, 120, 160) : Color.FromArgb(60, 60, 60),
                ForeColor = Color.FromArgb(224, 224, 224),
                Enabled = enabled
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.Click += onClick;
            return btn;
        }

        /// <summary>创建目标面板中的 × 删除按钮。尺寸/位置使用基准值。</summary>
        private Button CreateRemoveButton(int w, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = "×",
                Size = new Size(28, 24),
                Location = new Point(w - 34, 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),                 ForeColor = Color.FromArgb(224, 224, 224)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.Click += onClick;
            return btn;
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

        private void UpdateUI()
        {
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            _lblStatus.Text = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 个监控中, {pending} 个待激活"
                : $"● 监控中 — {active} 个窗口";

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
                kv.Value.BackColor = sel ? Color.FromArgb(90, 120, 160) : Color.FromArgb(60, 60, 60);
            }
            // 高亮选中面板
            foreach (var e in _entries)
                if (e.UIPanel != null)
                    e.UIPanel.BackColor = (!global && _selectedTarget != null && _selectedTarget.Equals(e.Info))
                        ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40);
            foreach (var kv in _pendingPanels)
                kv.Value.BackColor = (!global && _selectedTarget != null && _selectedTarget.Equals(kv.Key))
                    ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40);
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
        // 托盘
        // ════════════════════════════════════════════════════════════

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = _appIcon, Text = $"暗幕 v{AppVersion}", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            string s = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 监控中, {pending} 待激活"
                : $"● 监控中 — {active} 窗口";
            _menu.Items.Add(s).Enabled = false;
            _menu.Items.Add("-");
            _menu.Items.Add(Visible ? "最小化到托盘" : "打开设置窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => _chkEnabled.Checked = !_chkEnabled.Checked);
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (_, _) => { _reallyQuit = true; Close(); });
        }

        private void ToggleWindow()
        {
            if (Visible) Hide();
            else { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            { _settings.Save(); e.Cancel = true; Hide(); }
        }

        // ════════════════════════════════════════════════════════════
        // 核心操作（UI 按钮回调）
        // ════════════════════════════════════════════════════════════

        private void ToggleEnabled()
        {
            // 直接读 checkbox 状态，避免 _settings.Enabled 翻转与 UI 不一致
            bool enable = _chkEnabled.Checked;
            _settings.Enabled = enable;
            _settings.Save();
            if (enable)
            {
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    if (_entries.Any(e => e.Info == t)) RemovePendingUI(t);
                    else if (!_pendingPanels.ContainsKey(t)) AddPendingUI(t);
                }
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            else
            {
                foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Plate.HidePlate(); }
            }
            UpdateUI();
        }

        private void ToggleGlobalTransparency()
        {
            bool g = _chkGlobalTransparency.Checked;
            _settings.GlobalTransparency = g;
            if (!g)
            {
                // 关闭全局：把当前全局值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.BackgroundAlpha = _settings.BackgroundAlpha;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            foreach (var e in _entries) ApplyMaskNow(e);
        }

        private void ToggleGlobalCornerRadius()
        {
            bool g = _chkGlobalCornerRadius.Checked;
            _settings.GlobalCornerRadius = g;
            if (!g)
            {
                // 关闭全局：把当前全局圆角值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.CornerRadius = _settings.CornerRadius;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            // 刷新所有底板的圆角
            foreach (var e in _entries)
            {
                bool useGlobal = _settings.GlobalCornerRadius;
                e.Plate.CornerRadius = useGlobal ? _settings.CornerRadius : e.Info.CornerRadius;
                e.Tracker.RefreshForeground();
            }
        }

        private void SetBgAlpha(int value)
        {
            value = Math.Clamp(value, 0, 100);
            if (_settings.GlobalTransparency)
                _settings.BackgroundAlpha = value;
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.BackgroundAlpha = value;
            }
            foreach (var e in _entries) ApplyMaskNow(e);
            _lblBgAlpha.Text = $"{value}%";
            // 200ms 防抖：停止拖动后再写盘
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void SetCornerRadius(int value)
        {
            value = Math.Clamp(value, 0, 20);
            if (_settings.GlobalCornerRadius)
            {
                _settings.CornerRadius = value;
                foreach (var e in _entries)
                    SetPlateCornerRadius(e, value);
            }
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.CornerRadius = value;
                foreach (var e in _entries)
                {
                    if (e.Info == _selectedTarget)
                    {
                        SetPlateCornerRadius(e, value);
                        break;
                    }
                }
            }
            _lblCornerRadius.Text = value == 0 ? "关" : $"{value}px";
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private static void SetPlateCornerRadius(TargetEntry e, int value)
        {
            e.Plate.CornerRadius = value;
            e.Tracker.RefreshForeground();
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName ?? ""; } catch { }

            string title = "";
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                title = sb.ToString();
            }
            var info = new TargetInfo { ProcessName = procName, WindowTitle = title };

            // 以当前全局值作为该窗口独立配置的起点
            info.BackgroundAlpha = _settings.BackgroundAlpha;
            info.CornerRadius = _settings.CornerRadius;

            if (_settings.Targets.Contains(info))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);

            if (_settings.Enabled)
            {
                TryBindTarget(info);
                var entry = _entries.FirstOrDefault(e => e.Info == info);
                if (entry != null) ApplyMaskNow(entry);
                else AddPendingUI(info);
            }
            else AddPendingUI(info);
            // 非全局模式下，自动选中刚添加的窗口
            if (!_settings.GlobalTransparency || !_settings.GlobalCornerRadius) _selectedTarget = info;
            UpdateUI();
        }

        private void RefindAllWindows()
        {
            UnbindAll();
            foreach (var t in _settings.Targets)
            {
                TryBindTarget(t);
                if (!_entries.Any(e => e.Info == t)) AddPendingUI(t);
            }
            UpdateUI();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string v = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.1.0";
            MessageBox.Show(
                $"暗幕 v{v}\n\n" +
                "给任意窗口设置透明度、并在其正下方垫纯黑的常驻小工具。\n" +
                "支持多窗口同时控制。\n\n" +
                "• 配置: 与 exe 同目录 WindowTinter.settings.json\n" +
                "• 图标: app.ico 与 exe 同目录\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }

        private void SaveSettings()
        {
            _settings.Save();
        }

        // ════════════════════════════════════════════════════════════
        // WinEvent
        // ════════════════════════════════════════════════════════════

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshForeground(); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            // 目标特定事件：在 BeginInvoke 内读取 _entries，避免跨线程访问非安全集合
            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                try { BeginInvoke(new Action(() =>
                {
                    var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                    if (match != null) match.Tracker.RefreshNow();
                })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
