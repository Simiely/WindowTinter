using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    internal class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly InvertLens _invert;

        // 多窗口支持：每个目标一个 tracker + mask
        private readonly List<TargetEntry> _entries = new();

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;

        // UI 控件
        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnAdd;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private RadioButton _rbMask, _rbInvert;
        private TrackBar _tbAlpha;
        private Label _lblAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkAlwaysDim;
        private bool _reallyQuit;

        public MainForm()
        {
            _settings = Settings.Load();
            _invert = new InvertLens();

            Text = "WindowTinter";
            ClientSize = new Size(470, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Load += OnLoad;
            FormClosing += OnFormClosing;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            DebugLog.Info($"WindowTinter 启动 (Alpha={_settings.Alpha}, Mode={_settings.Mode}, Enabled={_settings.Enabled}, Targets={_settings.Targets.Count})");

            BuildTray();
            BuildUI();
            InstallWinEventHook();

            // 从配置恢复所有目标
            if (_settings.Enabled && _settings.Targets.Count > 0)
            {
                foreach (var t in _settings.Targets)
                    TryBindTarget(t);
            }

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        // ── 目标条目 ──────────────────────────────────────────────

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public MaskOverlay Mask;
            public Panel UIPanel;
            public Label Label;
        }

        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var mask = new MaskOverlay();
            tracker.OnUpdate += (r, visible, hrgn, _) =>
            {
                try
                {
                    if (hrgn != IntPtr.Zero) Native.DeleteObject(hrgn);
                    if (!_settings.Enabled || !visible) { mask.Hide(); return; }
                    if (_settings.Mode == "Invert") return;

                    // AlwaysDim 关闭时：目标不在前台则隐藏蒙版，避免遮挡上层窗口
                    if (!_settings.AlwaysDim && Native.GetForegroundWindow() != tracker.TargetHandle) { mask.Hide(); return; }

                    mask.Alpha = (byte)_settings.Alpha;
mask.AlignTo(r);
                }
                catch (Exception ex) { DebugLog.Error("TargetUpdate 异常", ex); }
            };

            return new TargetEntry { Info = info, Tracker = tracker, Mask = mask };
        }

        private void TryBindTarget(TargetInfo info)
        {
            var h = TargetTracker.FindByTitleAndProcess(info.WindowTitle, info.ProcessName);
            if (h == IntPtr.Zero) return;

            // 避免重复绑定
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return;

            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            _entries.Add(entry);

            AddTargetUI(info, entry);
            RefreshOwnWindows();

            DebugLog.Info($"已绑定窗口: {info}");
        }

        private void AddTargetUI(TargetInfo info, TargetEntry entry)
        {
            var pnl = new Panel { Size = new Size(_pnlTargets.ClientSize.Width - 6, 32), Margin = new Padding(0, 0, 0, 3) };
            var lbl = new Label
            {
                Text = $"  {info}", AutoSize = true,
                Location = new Point(4, 8), MaximumSize = new Size(300, 20)
            };
            pnl.Controls.Add(lbl);

            var btnRemove = new Button { Text = "×", Size = new Size(28, 24), Location = new Point(pnl.Width - 34, 4), FlatStyle = FlatStyle.Flat };
            int idx = _entries.IndexOf(entry);
            btnRemove.Click += (s, ev) =>
            {
                _entries.Remove(entry);
                _pnlTargets.Controls.Remove(pnl);
                entry.Tracker.Dispose();
                entry.Mask.Dispose();
                _settings.Targets.Remove(info);
                _settings.Save();
                RefreshOwnWindows();
                UpdateUI();
                DebugLog.Info($"已移除窗口: {info}");
            };
            pnl.Controls.Add(btnRemove);

            entry.UIPanel = pnl;
            entry.Label = lbl;
            _pnlTargets.Controls.Add(pnl);
            _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2); // 保持在添加按钮之前
        }

        private void RefreshOwnWindows()
        {
            var own = new List<IntPtr>();
            foreach (var e in _entries) own.Add(e.Mask.Handle);
            foreach (var h in _invert.OwnHandles) own.Add(h);
            foreach (var e in _entries) e.Tracker.OwnWindows = own.ToArray();
        }

        // ── 设置界面 ──────────────────────────────────────────────

        private void BuildUI()
        {
            int y = 10, pad = 10;

            // ── 状态 ──
            var gbStatus = new GroupBox { Text = "状态", Location = new Point(pad, y), Size = new Size(434, 50) };
            _lblStatus = new Label { Location = new Point(pad, 20), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            gbStatus.Controls.Add(_lblStatus);
            Controls.Add(gbStatus);
            y += 56;

            // ── 目标窗口列表 ──
            var gbTarget = new GroupBox { Text = "目标窗口", Location = new Point(pad, y), Size = new Size(434, 160) };
            _pnlTargets = new FlowLayoutPanel
            {
                Location = new Point(pad, 18), Size = new Size(416, 110),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false
            };
            gbTarget.Controls.Add(_pnlTargets);

            _btnAdd = new Button { Text = "+ 添加窗口", Location = new Point(pad, 132), Size = new Size(95, 24) };
            _btnAdd.Click += (s, ev) => PickWindow();
            gbTarget.Controls.Add(_btnAdd);
            _btnRefind = new Button { Text = "🔄 重新查找", Location = new Point(110, 132), Size = new Size(95, 24) };
            _btnRefind.Click += (s, ev) => RefindAllWindows();
            gbTarget.Controls.Add(_btnRefind);

            Controls.Add(gbTarget);
            y += 166;

            // ── 启用 ──
            _chkEnabled = new CheckBox
            {
                Text = "启用覆盖", Location = new Point(pad + 4, y), AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), Checked = _settings.Enabled
            };
            _chkEnabled.CheckedChanged += (s, ev) => ToggleEnabled();
            Controls.Add(_chkEnabled);
            y += 28;

            // ── 模式 ──
            var gbMode = new GroupBox { Text = "模式", Location = new Point(pad, y), Size = new Size(434, 50) };
            _rbMask = new RadioButton { Text = "深色蒙版", Location = new Point(pad, 20), AutoSize = true, Checked = _settings.Mode == "Mask" };
            _rbMask.CheckedChanged += (s, ev) => { if (_rbMask.Checked) SetMode("Mask"); };
            _rbInvert = new RadioButton { Text = "真·反色 (实验，仅首个窗口)", Location = new Point(200, 20), AutoSize = true, Checked = _settings.Mode == "Invert" };
            _rbInvert.CheckedChanged += (s, ev) => { if (_rbInvert.Checked) SetMode("Invert"); };
            gbMode.Controls.Add(_rbMask);
            gbMode.Controls.Add(_rbInvert);
            Controls.Add(gbMode);
            y += 56;

            // ── 透明度 ──
            var gbAlpha = new GroupBox { Text = "透明度", Location = new Point(pad, y), Size = new Size(434, 65) };
            _tbAlpha = new TrackBar { Location = new Point(pad, 18), Size = new Size(350, 40), Minimum = 0, Maximum = 19, Value = (int)(_settings.Alpha * 19.0 / 255 + 0.5), TickFrequency = 1, SmallChange = 1, LargeChange = 3 };
            _tbAlpha.ValueChanged += (s, ev) => SetAlpha((int)(_tbAlpha.Value * 255.0 / 19 + 0.5));
            gbAlpha.Controls.Add(_tbAlpha);
            _lblAlpha = new Label { Location = new Point(376, 24), AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
            gbAlpha.Controls.Add(_lblAlpha);
            Controls.Add(gbAlpha);
            y += 71;

            // ── 系统 ──
            var rowY = y + 2;
            _chkStartup = new CheckBox { Text = "开机自启", Location = new Point(pad + 4, rowY), AutoSize = true, Checked = _settings.StartWithWindows };
            _chkStartup.CheckedChanged += (s1, ev) => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); _settings.Save(); };
            Controls.Add(_chkStartup);

            _chkMinimizeTray = new CheckBox { Text = "关闭窗口时最小化到托盘（不勾选则直接退出）", Location = new Point(pad + 4, rowY + 24), AutoSize = true, Checked = _settings.MinimizeToTray };
            _chkMinimizeTray.CheckedChanged += (s1, ev) => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; _settings.Save(); };
            Controls.Add(_chkMinimizeTray);

            _chkAlwaysDim = new CheckBox { Text = "后台仍遮挡（目标不在前台时也加蒙版）", Location = new Point(pad + 4, rowY + 48), AutoSize = true, Checked = _settings.AlwaysDim };
            _chkAlwaysDim.CheckedChanged += (s1, ev) => { _settings.AlwaysDim = _chkAlwaysDim.Checked; _settings.Save(); };
            Controls.Add(_chkAlwaysDim);
            rowY += 78;

            Controls.Add(MakeButton("📂 配置文件夹", pad, rowY, 120, OpenConfigFolder));
            Controls.Add(MakeButton("📋 查看日志", 134, rowY, 100, OpenLog));
            Controls.Add(MakeButton("ℹ 关于", 240, rowY, 70, ShowAbout));

            // 恢复已有条目的 UI
            for (int i = 0; i < _entries.Count; i++)
                AddTargetUI(_entries[i].Info, _entries[i]);

            ApplyDarkTheme();
        }

        private static Button MakeButton(string text, int x, int y, int w, Action onClick)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30) };
            btn.Click += (s, ev) => onClick();
            return btn;
        }

        // ── 深色主题 ──────────────────────────────────────────────

        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.FromArgb(224, 224, 224);
            var panelBg = Color.FromArgb(40, 40, 40);

            BackColor = bg; ForeColor = fg;
            if (IsHandleCreated) { int dark = 1; Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4); }

            ThemeAll(this, bg, fg, panelBg);
        }

        private static void ThemeAll(Control parent, Color bg, Color fg, Color panelBg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is GroupBox || c is Panel || c is FlowLayoutPanel) { c.BackColor = panelBg; c.ForeColor = fg; }
                else if (c is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = fg; btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (c is CheckBox || c is RadioButton) { c.BackColor = bg; c.ForeColor = fg; }
                else if (c is Label) { c.BackColor = c.Parent is Panel ? panelBg : bg; c.ForeColor = c.ForeColor == Color.Gray ? Color.FromArgb(140, 140, 140) : fg; }
                else if (c is TextBox) { c.BackColor = Color.FromArgb(50, 50, 50); c.ForeColor = fg; }
                else if (c is TrackBar) { c.BackColor = panelBg; }
                else { c.BackColor = bg; c.ForeColor = fg; }
                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        // ── UI 同步 ───────────────────────────────────────────────

        private void UpdateUI()
        {
            if (!_settings.Enabled) _lblStatus.Text = "⏸ 已暂停";
            else if (_entries.Count == 0) _lblStatus.Text = "○ 等待选择窗口…";
            else _lblStatus.Text = $"● 监控中 — {_entries.Count} 个窗口";

            _chkEnabled.Checked = _settings.Enabled;
            _rbMask.Checked = _settings.Mode == "Mask";
            _rbInvert.Checked = _settings.Mode == "Invert";
            int sv = (int)(_settings.Alpha * 19.0 / 255 + 0.5);
            if (_tbAlpha.Value != sv) _tbAlpha.Value = sv;
            _lblAlpha.Text = $"Lv.{_tbAlpha.Value} ({_settings.Alpha}/255)";
            _chkStartup.Checked = _settings.StartWithWindows;
            _chkMinimizeTray.Checked = _settings.MinimizeToTray;
            _chkAlwaysDim.Checked = _settings.AlwaysDim;
            RefreshTrayMenu();
        }

        // ── 托盘 ──────────────────────────────────────────────────

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "WindowTinter", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (s, e) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            string s = !_settings.Enabled ? "⏸ 已暂停" : _entries.Count == 0 ? "○ 等待选择窗口…" : $"● 监控中 — {_entries.Count} 窗口";
            _menu.Items.Add(s).Enabled = false; _menu.Items.Add("-");
            _menu.Items.Add(Visible ? "最小化到托盘" : "打开设置窗口", null, (s2, e) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (s2, e) => ToggleEnabled());
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (s2, e) => { _reallyQuit = true; Close(); });
        }

        private void ToggleWindow() { if (Visible) Hide(); else { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); } }
        private void OnFormClosing(object sender, FormClosingEventArgs e) { if (!_reallyQuit && _settings.MinimizeToTray) { e.Cancel = true; Hide(); } }

        // ── 核心操作 ──────────────────────────────────────────────

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets) TryBindTarget(t);
                // 立即刷新所有已绑定蒙版
                foreach (var e in _entries)
                {
                    if (e.Tracker.TargetHandle != IntPtr.Zero && Native.IsWindow(e.Tracker.TargetHandle)
                        && Native.IsWindowVisible(e.Tracker.TargetHandle) && !Native.IsIconic(e.Tracker.TargetHandle))
                    {
                        Native.GetWindowRect(e.Tracker.TargetHandle, out Native.RECT r);
                        e.Mask.Alpha = (byte)_settings.Alpha;
                        e.Mask.AlignTo(r);
                    }
                }
            }
            else
            {
                foreach (var e in _entries) e.Mask.Hide();
                _invert.Hide();
            }
            _settings.Save(); UpdateUI();
        }

        private void SetMode(string mode)
        {
            _settings.Mode = mode;
            if (mode == "Invert") _invert.Start();
            else _invert.Hide();
            _settings.Save(); UpdateUI();
        }

        private void SetAlpha(int value)
        {
            _settings.Alpha = Math.Max(10, Math.Min(255, value));
            foreach (var e in _entries)
            {
                e.Mask.Alpha = (byte)_settings.Alpha;
                if (e.Tracker.TargetHandle != IntPtr.Zero && Native.IsWindow(e.Tracker.TargetHandle))
                {
                    Native.GetWindowRect(e.Tracker.TargetHandle, out Native.RECT r);
                    e.Mask.AlignTo(r);
                }
            }
            _settings.Save();
            int sv = (int)(_settings.Alpha * 19.0 / 255 + 0.5);
            if (_tbAlpha.Value != sv) _tbAlpha.Value = sv;
            _lblAlpha.Text = $"Lv.{_tbAlpha.Value} ({_settings.Alpha}/255)";
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            uint pid; Native.GetWindowThreadProcessId(picker.SelectedHandle, out pid);
            var info = new TargetInfo();
            try { var proc = Process.GetProcessById((int)pid); info.ProcessName = proc.ProcessName + ".exe"; } catch { info.ProcessName = ""; }
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0) { var sb = new System.Text.StringBuilder(len + 1); Native.GetWindowText(picker.SelectedHandle, sb, len + 1); info.WindowTitle = sb.ToString(); }

            // 避免重复
            if (_settings.Targets.Any(t => t.WindowTitle == info.WindowTitle && t.ProcessName == info.ProcessName))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);
            _settings.Save();
            DebugLog.Info($"添加窗口: {info}");

            if (_settings.Enabled)
                TryBindTarget(info);

            UpdateUI();
        }

        private void RefindAllWindows()
        {
            // 释放旧条目
            foreach (var e in _entries) { e.Mask.Hide(); e.Tracker.Dispose(); e.Mask.Dispose(); }
            _entries.Clear();
            _pnlTargets.Controls.Clear();
            _pnlTargets.Controls.Add(_btnAdd);
            _pnlTargets.Controls.Add(_btnRefind);
            _invert.Hide();

            // 重新查找全部
            foreach (var t in _settings.Targets)
                TryBindTarget(t);

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
            MessageBox.Show("WindowTinter v2.2\n\n给任意窗口叠加深色半透明蒙版的常驻小工具。\n支持多窗口同时覆盖。\n\n• 配置: exe 同目录 WindowTinter.settings.json\n• 日志: exe 同目录 WindowTinter.debug.log\n\nhttps://github.com/Simiely/WindowTinter", "关于");
        }

        // ── WinEvent ──────────────────────────────────────────────

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_ZORDERCHANGES,
                IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            bool relevant = (eventType is Native.EVENT_SYSTEM_FOREGROUND or Native.EVENT_OBJECT_ZORDERCHANGES);
            if (!relevant)
            {
                relevant = _entries.Any(e => e.Tracker.TargetHandle == hwnd) &&
                    eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY;
            }
            if (relevant)
            {
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshNow(); })); }
                catch { }
            }
        }

        private void Quit()
        {
            DebugLog.Info("WindowTinter 退出");
            _reallyQuit = true; _tray.Visible = false;
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            foreach (var e in _entries) { e.Mask.Dispose(); e.Tracker.Dispose(); }
            _invert.Dispose();
            Application.Exit();
        }

        // ── 入口 ──────────────────────────────────────────────────

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.ThreadException += (s, e) => DebugLog.Error("UI线程异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => DebugLog.Error("未处理异常", e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }
    }
}
