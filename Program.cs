using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置的自定义 TrackBar。</summary>
    internal class JumpTrackBar : TrackBar
    {
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

    internal class MainForm : Form
    {
        // ── 核心状态 ──────────────────────────────────────────────

        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;

        // ── UI 控件 ────────────────────────────────────────────────

        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbAlpha;
        private Label _lblAlpha;
        private TrackBar _tbBgAlpha;
        private Label _lblBgAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkMinimizeTray;

        // ── 窗口 ───────────────────────────────────────────────────

        public MainForm()
        {
            _settings = Settings.Load();

            Text = "WindowTinter";
            ClientSize = new Size(470, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Load += OnLoad;
            Shown += OnShown;
            Activated += OnActivated;
            FormClosing += OnFormClosing;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            DebugLog.Info($"WindowTinter 启动 (Alpha={_settings.Alpha}, Mode={_settings.Mode}, Enabled={_settings.Enabled}, Targets={_settings.Targets.Count})");
            BuildTray();
            BuildUI();
            InstallWinEventHook();

            // 3 秒一次检查是否有目标窗口新启动但未绑定
            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;

                // 清理窗口已销毁的条目
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];
                    if (!Native.IsWindow(e.Tracker.TargetHandle) && e.Tracker.TargetHandle != IntPtr.Zero)
                    {
                        e.Mask.Hide(); e.Tracker.Dispose(); e.Mask.Dispose();
                        _entries.RemoveAt(i);
                        if (e.UIPanel != null) _pnlTargets.Controls.Remove(e.UIPanel);
                    }
                }

                // 查找未绑定目标
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    var entry = _entries.FirstOrDefault(e => e.Info == t);
                    if (entry != null) ApplyMaskNow(entry);
                }
            };
            _autoBindTimer.Start();

            if (_settings.Enabled)
                foreach (var t in _settings.Targets)
                    TryBindTarget(t);

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        private void OnShown(object _, EventArgs __)
        {
            foreach (var e in _entries) e.Tracker.RefreshNow();
            // 用一次性定时器推迟 100ms，确保消息泵完整运转后 UpdateLayeredWindow 稳定
            var t = new Timer { Interval = 100 };
            t.Tick += (s, args) => { t.Stop(); t.Dispose(); foreach (var e in _entries) ApplyMaskNow(e); };
            t.Start();
        }

        /// <summary>点击 WindowTinter 自身时，WINEVENT_SKIPOWNPROCESS 会跳过前台事件，
        /// 通过 Activated 事件补发 RefreshForeground，让目标切到透明状态。
        /// BeginInvoke 异步化避免 SetTargetAlpha 跨进程阻塞 UI 线程。</summary>
        private bool _inActivated;
        private void OnActivated(object _, EventArgs __)
        {
            if (_inActivated) return;
            _inActivated = true;
            BeginInvoke(new Action(() =>
            {
                try { foreach (var e in _entries) e.Tracker.RefreshForeground(); }
                finally { _inActivated = false; }
            }));
        }

        // ════════════════════════════════════════════════════════════
        // 目标条目管理
        // ════════════════════════════════════════════════════════════

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public MaskOverlay Mask;
            public Panel UIPanel;
        }

        /// <summary>创建条目并挂载 OnUpdate——所有蒙版显示逻辑的唯一入口。</summary>
        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var mask = new MaskOverlay();

            byte _lastBgAlpha = 255;

            tracker.OnUpdate += (r, visible) =>
            {
                try
                {
                    bool fg = Native.GetForegroundWindow() == tracker.TargetHandle;
                    if (!_settings.Enabled || !visible)
                    {
                        mask.Hide();
                        if (_lastBgAlpha != 255) { SetTargetAlpha(tracker.TargetHandle, 255); _lastBgAlpha = 255; }
                        return;
                    }
                    if (fg)
                    {
                        if (_lastBgAlpha != 255) { SetTargetAlpha(tracker.TargetHandle, 255); _lastBgAlpha = 255; }
                        mask.Alpha = (byte)(_settings.Alpha * 255 / 100);
                        mask.AlignTo(r);
                    }
                    else
                    {
                        mask.Hide();
                        byte targetAlpha = (byte)((100 - _settings.BackgroundAlpha) * 255 / 100);
                        if (_lastBgAlpha != targetAlpha)
                        {
                            SetTargetAlpha(tracker.TargetHandle, targetAlpha);
                            _lastBgAlpha = targetAlpha;
                        }
                    }
                }
                catch (Exception ex) { DebugLog.Error("OnUpdate", ex); }
            };

            return new TargetEntry { Info = info, Tracker = tracker, Mask = mask };
        }

        private static void SetTargetAlpha(IntPtr hwnd, byte alpha)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                    bool hasLayered = (ex & Native.WS_EX_LAYERED) != 0;

                    if (alpha >= 255)
                    {
                        if (hasLayered)
                        {
                            Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
                            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_LAYERED);
                        }
                    }
                    else
                    {
                        if (!hasLayered)
                            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                        Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
                    }
                }
                catch { }
            });
        }

        /// <summary>触发刷新——走 OnUpdate 完整路径（前景蒙版/后台透明）。</summary>
        private void ApplyMaskNow(TargetEntry e)
        {
            e.Tracker.RefreshForeground();
        }

        private static bool TryGetTargetRect(TargetEntry e, out Native.RECT r)
        {
            r = default;
            if (e.Tracker.TargetHandle == IntPtr.Zero || !Native.IsWindow(e.Tracker.TargetHandle)) return false;
            if (!Native.IsWindowVisible(e.Tracker.TargetHandle) || Native.IsIconic(e.Tracker.TargetHandle)) return false;
            Native.GetWindowRect(e.Tracker.TargetHandle, out r);
            return r.Width > 0 && r.Height > 0;
        }

        private IntPtr[] OwnHandles()
        {
            var own = new List<IntPtr>();
            foreach (var e in _entries) own.Add(e.Mask.Handle);
            return own.ToArray();
        }

        private void RefreshOwnWindows()
        {
            var own = OwnHandles();
            foreach (var e in _entries) e.Tracker.OwnWindows = own;
        }

        private void TryBindTarget(TargetInfo info)
        {
            var h = TargetTracker.FindByTitleAndProcess(info.WindowTitle, info.ProcessName);
            if (h == IntPtr.Zero) return;

            // 已绑定到同一个 handle → 跳过
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return;

            // 有该目标的旧条目（窗口关闭后 reopen）→ 更新 handle 复用
            var stale = _entries.FirstOrDefault(e => e.Info == info && !Native.IsWindow(e.Tracker.TargetHandle));
            if (stale != null)
            {
                stale.Tracker.TargetHandle = h;
                stale.Tracker.RefreshNow();
                DebugLog.Info($"重新绑定窗口: {info}");
                return;
            }

            // 新绑定
            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            _entries.Add(entry);

            AddTargetUI(entry);
            RefreshOwnWindows();
            DebugLog.Info($"已绑定窗口: {info}");
        }

        private void AddTargetUI(TargetEntry entry)
        {
            int w = _pnlTargets.ClientSize.Width - 6;
            var pnl = new Panel { Size = new Size(w, 32), Margin = new Padding(0, 0, 0, 3) };

            var lbl = new Label
            {
                Text = $"  {entry.Info}", AutoSize = true,
                Location = new Point(4, 8), MaximumSize = new Size(300, 20)
            };
            pnl.Controls.Add(lbl);

            var btnRemove = new Button
            {
                Text = "×", Size = new Size(28, 24),
                Location = new Point(w - 34, 4), FlatStyle = FlatStyle.Flat
            };
            btnRemove.Click += (_, _) => RemoveEntry(entry, pnl);
            pnl.Controls.Add(btnRemove);

            entry.UIPanel = pnl;
            _pnlTargets.Controls.Add(pnl);
            _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        private void RemoveEntry(TargetEntry entry, Panel pnl)
        {
            _entries.Remove(entry);
            _pnlTargets.Controls.Remove(pnl);
            SetTargetAlpha(entry.Tracker.TargetHandle, 255);
            entry.Tracker.Dispose();
            entry.Mask.Dispose();
            _settings.Targets.Remove(entry.Info);
            _settings.Save();
            RefreshOwnWindows();
            UpdateUI();
            DebugLog.Info($"已移除窗口: {entry.Info}");
        }

        private void UnbindAll()
        {
            foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Mask.Hide(); e.Tracker.Dispose(); e.Mask.Dispose(); }
            _entries.Clear();
            _pnlTargets.Controls.Clear();
            _pnlTargets.Controls.Add(_btnRefind);
        }

        // ════════════════════════════════════════════════════════════
        // 设置界面构建
        // ════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            int y = 10, pad = 10;
            const int GW = 434; // group box width

            AddGroup("状态", pad, ref y, 50, GW, gb =>
                _lblStatus = AddLabel(gb, pad, 20, FontStyle.Bold, 10f));

            AddGroup("目标窗口", pad, ref y, 160, GW, gb =>
            {
                _pnlTargets = new FlowLayoutPanel
                {
                    Location = new Point(pad, 18), Size = new Size(416, 110),
                    AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false
                };
                gb.Controls.Add(_pnlTargets);

                AddButton(gb, "+ 添加窗口", pad, 132, 95, PickWindow);
                _btnRefind = AddButton(gb, "🔄 重新查找", 110, 132, 95, RefindAllWindows);
            });

            _chkEnabled = AddCheck(this, "启用覆盖", pad + 4, y, FontStyle.Bold, _settings.Enabled, ToggleEnabled);
            y += 28;

            AddGroup("透明度", pad, ref y, 120, GW, gb =>
            {
                gb.Controls.Add(new Label { Text = "蒙版 (前台):", Location = new Point(pad, 18), AutoSize = true });
                _tbAlpha = new JumpTrackBar
                {
                    Location = new Point(90, 16), Size = new Size(280, 40),
                    Minimum = 0, Maximum = 100, TickFrequency = 10,
                    SmallChange = 5, LargeChange = 20,
                    Value = _settings.Alpha
                };
                _tbAlpha.ValueChanged += (_, _) => SetAlpha(_tbAlpha.Value);
                gb.Controls.Add(_tbAlpha);
                _lblAlpha = new Label { Location = new Point(376, 24), AutoSize = true };
                gb.Controls.Add(_lblAlpha);

                gb.Controls.Add(new Label { Text = "窗口 (后台):", Location = new Point(pad, 56), AutoSize = true });
                _tbBgAlpha = new JumpTrackBar
                {
                    Location = new Point(90, 54), Size = new Size(280, 40),
                    Minimum = 0, Maximum = 100, TickFrequency = 10,
                    SmallChange = 5, LargeChange = 20,
                    Value = _settings.BackgroundAlpha
                };
                _tbBgAlpha.ValueChanged += (_, _) => { _settings.BackgroundAlpha = _tbBgAlpha.Value; foreach (var e in _entries) e.Tracker.RefreshForeground(); _lblBgAlpha.Text = $"{_tbBgAlpha.Value}%"; };
                gb.Controls.Add(_tbBgAlpha);
                _lblBgAlpha = new Label { Location = new Point(376, 62), AutoSize = true };
                gb.Controls.Add(_lblBgAlpha);
            });

            // 系统选项
            int rowY = y + 2;
            _chkStartup = AddCheck(this, "开机自启", pad + 4, rowY, FontStyle.Regular, _settings.StartWithWindows,
                () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); });
            _chkMinimizeTray = AddCheck(this, "关闭窗口时最小化到托盘（不勾选则直接退出）", pad + 4, rowY + 24, FontStyle.Regular,
                _settings.MinimizeToTray, () => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; });
            rowY += 54;

            AddButton(this, "📂 配置文件夹", pad, rowY, 120, OpenConfigFolder);
            AddButton(this, "📋 查看日志", 134, rowY, 100, OpenLog);
            AddButton(this, "ℹ 关于", 240, rowY, 70, ShowAbout);
            AddButton(this, "💾 保存配置", 316, rowY, 110, SaveSettings);

            // 恢复已有条目
            for (int i = 0; i < _entries.Count; i++)
                AddTargetUI(_entries[i]);

            ApplyDarkTheme();
        }

        // ── 控件辅助方法 ──────────────────────────────────────────

        private static void AddGroup(string text, int x, ref int y, int h, int w, Action<GroupBox> build)
        {
            var gb = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            build(gb);
            Application.OpenForms[0].Controls.Add(gb);
            y += h + 6;
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

        private static RadioButton AddRadio(Control parent, string text, int x, int y, bool initial, Action onChange)
        {
            var rb = new RadioButton { Text = text, Location = new Point(x, y), AutoSize = true, Checked = initial };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) onChange(); };
            parent.Controls.Add(rb);
            return rb;
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
            _lblStatus.Text = !_settings.Enabled ? "⏸ 已暂停"
                : _entries.Count == 0 ? "○ 等待选择窗口…"
                : $"● 监控中 — {_entries.Count} 个窗口";

            _chkEnabled.Checked = _settings.Enabled;

            int sv = _settings.Alpha;
            if (_tbAlpha.Value != sv) _tbAlpha.Value = sv;
            _lblAlpha.Text = $"{_tbAlpha.Value}%";
            _lblBgAlpha.Text = $"{_settings.BackgroundAlpha}%";

            _chkStartup.Checked = _settings.StartWithWindows;
            _chkMinimizeTray.Checked = _settings.MinimizeToTray;
            RefreshTrayMenu();
        }

        // ════════════════════════════════════════════════════════════
        // 托盘
        // ════════════════════════════════════════════════════════════

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "WindowTinter", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            string s = !_settings.Enabled ? "⏸ 已暂停" : _entries.Count == 0 ? "○ 等待选择窗口…" : $"● 监控中 — {_entries.Count} 窗口";
            _menu.Items.Add(s).Enabled = false;
            _menu.Items.Add("-");
            _menu.Items.Add(Visible ? "最小化到托盘" : "打开设置窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => ToggleEnabled());
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
            if (!_reallyQuit && _settings.MinimizeToTray) { e.Cancel = true; Hide(); }
        }

        // ════════════════════════════════════════════════════════════
        // 核心操作
        // ════════════════════════════════════════════════════════════

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets) TryBindTarget(t);
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            else
            {
                foreach (var e in _entries) { e.Mask.Hide(); SetTargetAlpha(e.Tracker.TargetHandle, 255); }
            }
            UpdateUI();
        }

        private void SetAlpha(int value)
        {
            _settings.Alpha = Math.Clamp(value, 0, 100);
            foreach (var e in _entries) ApplyMaskNow(e);
            _lblAlpha.Text = $"{_tbAlpha.Value}%";
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            var info = new TargetInfo();
            try { info.ProcessName = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch { }

            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                info.WindowTitle = sb.ToString();
            }

            if (_settings.Targets.Any(t => t.WindowTitle == info.WindowTitle && t.ProcessName == info.ProcessName))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);
            DebugLog.Info($"添加窗口: {info}");

            if (_settings.Enabled)
            {
                TryBindTarget(info);
                var entry = _entries.FirstOrDefault(e => e.Info == info);
                if (entry != null) ApplyMaskNow(entry);
            }
            UpdateUI();
        }

        private void RefindAllWindows()
        {
            UnbindAll();
            foreach (var t in _settings.Targets) TryBindTarget(t);
            UpdateUI();
            DebugLog.Info($"重新查找窗口: {_entries.Count}/{_settings.Targets.Count} 已绑定");
        }

        private void OpenConfigFolder()
        {
            try { Process.Start("explorer.exe", Path.GetDirectoryName(Environment.ProcessPath)); }
            catch (Exception ex) { DebugLog.Error("打开配置文件夹失败", ex); }
        }

        private void OpenLog()
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WindowTinter.debug.log");
                if (File.Exists(path)) Process.Start("notepad.exe", path);
                else MessageBox.Show("日志文件尚不存在。", "WindowTinter");
            }
            catch (Exception ex) { DebugLog.Error("打开日志失败", ex); }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "WindowTinter v2.3\n\n" +
                "给任意窗口叠加深色半透明蒙版的常驻小工具。\n" +
                "支持多窗口同时覆盖。\n\n" +
                "• 配置: exe 同目录 WindowTinter.settings.json\n" +
                "• 日志: exe 同目录 WindowTinter.debug.log\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }

        // ════════════════════════════════════════════════════════════
        // WinEvent
        // ════════════════════════════════════════════════════════════

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_ZORDERCHANGES,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                // 前台切换：强制刷新所有条目的蒙版/透明度状态
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshForeground(); })); }
                catch { }
                return;
            }

            if (eventType == Native.EVENT_OBJECT_ZORDERCHANGES)
            {
                // Z 序变化：全量轮询（带 rect 变更守卫，不改透明度，不会触发回环）
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshNow(); })); }
                catch { }
                return;
            }

            // 目标特定事件：仅处理匹配的 tracker
            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY)
            {
                var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == hwnd);
                if (match != null)
                {
                    try { BeginInvoke(new Action(match.Tracker.RefreshNow)); }
                    catch { }
                }
            }
        }

        private void SaveSettings()
        {
            _settings.Save();
            DebugLog.Info("配置已手动保存");
        }

        private void Quit()
        {
            DebugLog.Info("WindowTinter 退出");
            _settings.Save();
            _reallyQuit = true;
            _tray.Visible = false;
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Mask.Dispose(); e.Tracker.Dispose(); }
            Application.Exit();
        }

        // ════════════════════════════════════════════════════════════
        // 入口
        // ════════════════════════════════════════════════════════════

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.ThreadException += (_, e) => DebugLog.Error("UI线程异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => DebugLog.Error("未处理异常", e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }
    }
}
