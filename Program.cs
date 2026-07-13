using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置。</summary>
    internal class JumpTrackBar : TrackBar
    {
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDOWN)
            {
                int x = unchecked((short)((int)m.LParam & 0xFFFF));
                int cw = Width - 24;
                if (cw > 0) Value = Math.Clamp((x - 12) * (Maximum - Minimum) / cw + Minimum, Minimum, Maximum);
            }
            base.WndProc(ref m);
        }
    }

    internal class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;

        // UI
        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbAlpha;
        private Label _lblAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkMinimizeTray;

        // ════════════════════════════════════════════════════════════
        // 初始化
        // ════════════════════════════════════════════════════════════

        public MainForm()
        {
            _settings = Settings.Load();
            Text = "WindowTinter";
            ClientSize = new Size(470, 490);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Load += OnLoad;
            Shown += OnShown;
            FormClosing += OnFormClosing;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            DebugLog.Info($"WindowTinter v2.5 (Alpha={_settings.Alpha}%, Targets={_settings.Targets.Count}, Enabled={_settings.Enabled})");
            BuildTray();
            BuildUI();
            InstallWinEventHook();

            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) => AutoBindTick();
            _autoBindTimer.Start();

            if (_settings.Enabled)
                foreach (var t in _settings.Targets)
                    TryBindTarget(t);

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        private void OnShown(object _, EventArgs __) { foreach (var e in _entries) e.Tracker.RefreshNow(); }

        private void AutoBindTick()
        {
            if (!_settings.Enabled) return;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var en = _entries[i];
                if (!Native.IsWindow(en.Tracker.TargetHandle) && en.Tracker.TargetHandle != IntPtr.Zero)
                {
                    en.Mask.Hide(); en.Tracker.Dispose(); en.Mask.Dispose();
                    _entries.RemoveAt(i);
                    if (en.UIPanel != null) _pnlTargets.Controls.Remove(en.UIPanel);
                }
            }
            foreach (var t in _settings.Targets)
                if (!_entries.Any(e => e.Info == t)) TryBindTarget(t);
        }

        // ════════════════════════════════════════════════════════════
        // 目标条目
        // ════════════════════════════════════════════════════════════

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public MaskOverlay Mask;
            public Panel UIPanel;
        }

        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var mask = new MaskOverlay();

            tracker.OnUpdate += (r, visible) =>
            {
                try
                {
                    if (!_settings.Enabled || !visible) { mask.Hide(); return; }
                    if (Native.GetForegroundWindow() != tracker.TargetHandle) { mask.Hide(); return; }
                    mask.ShowDark(r, (byte)(_settings.Alpha * 255 / 100));
                }
                catch (Exception ex) { DebugLog.Error("OnUpdate", ex); }
            };

            return new TargetEntry { Info = info, Tracker = tracker, Mask = mask };
        }

        private void TryBindTarget(TargetInfo info)
        {
            var h = TargetTracker.FindByTitleAndProcess(info.WindowTitle, info.ProcessName);
            if (h == IntPtr.Zero) return;
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return;

            var stale = _entries.FirstOrDefault(e => e.Info == info && !Native.IsWindow(e.Tracker.TargetHandle));
            if (stale != null)
            {
                stale.Tracker.TargetHandle = h;
                stale.Tracker.RefreshNow();
                DebugLog.Info($"重新绑定: {info}");
                return;
            }

            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            _entries.Add(entry);
            AddTargetUI(entry);
            RefreshOwnWindows();
            DebugLog.Info($"已绑定: {info}");
        }

        private void ShowMask(TargetEntry e)
        {
            if (!TryGetTargetRect(e, out Native.RECT r)) return;
            e.Mask.ShowDark(r, (byte)(_settings.Alpha * 255 / 100));
        }

        private static bool TryGetTargetRect(TargetEntry e, out Native.RECT r)
        {
            r = default;
            return e.Tracker.TargetHandle != IntPtr.Zero
                && Native.IsWindow(e.Tracker.TargetHandle)
                && Native.IsWindowVisible(e.Tracker.TargetHandle)
                && !Native.IsIconic(e.Tracker.TargetHandle)
                && Native.GetWindowRect(e.Tracker.TargetHandle, out r)
                && r.Width > 0 && r.Height > 0;
        }

        private void RefreshOwnWindows()
        {
            var own = _entries.Select(e => e.Mask.Handle).ToArray();
            foreach (var e in _entries) e.Tracker.OwnWindows = own;
        }

        // ════════════════════════════════════════════════════════════
        // 目标 UI
        // ════════════════════════════════════════════════════════════

        private void AddTargetUI(TargetEntry entry)
        {
            int w = _pnlTargets.ClientSize.Width - 6;
            var pnl = new Panel { Size = new Size(w, 32), Margin = new Padding(0, 0, 0, 3) };
            pnl.Controls.Add(new Label { Text = $"  {entry.Info}", AutoSize = true, Location = new Point(4, 8), MaximumSize = new Size(300, 20) });
            var btn = new Button { Text = "×", Size = new Size(28, 24), Location = new Point(w - 34, 4), FlatStyle = FlatStyle.Flat };
            btn.Click += (_, _) => RemoveEntry(entry, pnl);
            pnl.Controls.Add(btn);
            entry.UIPanel = pnl;
            _pnlTargets.Controls.Add(pnl);
            _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        private void RemoveEntry(TargetEntry entry, Panel pnl)
        {
            _entries.Remove(entry); _pnlTargets.Controls.Remove(pnl);
            entry.Tracker.Dispose(); entry.Mask.Dispose();
            _settings.Targets.Remove(entry.Info); _settings.Save();
            RefreshOwnWindows(); UpdateUI();
        }

        private void UnbindAll()
        {
            foreach (var e in _entries) { e.Mask.Hide(); e.Tracker.Dispose(); e.Mask.Dispose(); }
            _entries.Clear(); _pnlTargets.Controls.Clear(); _pnlTargets.Controls.Add(_btnRefind);
        }

        // ════════════════════════════════════════════════════════════
        // UI 构建
        // ════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            int y = 10; const int P = 10, GW = 434;

            AddGroup("状态", P, ref y, 50, GW, gb =>
                _lblStatus = gb.AddChild(new Label { Location = new Point(P, 20), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) }));

            AddGroup("目标窗口", P, ref y, 160, GW, gb =>
            {
                _pnlTargets = new FlowLayoutPanel { Location = new Point(P, 18), Size = new Size(416, 110), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                gb.Controls.Add(_pnlTargets);
                gb.AddButton("+ 添加窗口", P, 132, 95, PickWindow);
                _btnRefind = gb.AddButton("🔄 重新查找", 110, 132, 95, RefindAllWindows);
            });

            _chkEnabled = AddCheck("启用覆盖", P + 4, y, FontStyle.Bold, _settings.Enabled, ToggleEnabled);
            y += 28;

            AddGroup("透明度", P, ref y, 65, GW, gb =>
            {
                _tbAlpha = new JumpTrackBar { Location = new Point(P, 18), Size = new Size(350, 40), Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 20, Value = _settings.Alpha };
                _tbAlpha.ValueChanged += (_, _) => SetAlpha(_tbAlpha.Value);
                gb.Controls.Add(_tbAlpha);
                _lblAlpha = new Label { Location = new Point(376, 24), AutoSize = true };
                gb.Controls.Add(_lblAlpha);
            });

            int rY = y + 2;
            _chkStartup = AddCheck("开机自启", P + 4, rY, FontStyle.Regular, _settings.StartWithWindows, () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); });
            _chkMinimizeTray = AddCheck("关闭时最小化到托盘", P + 4, rY + 24, FontStyle.Regular, _settings.MinimizeToTray, () => _settings.MinimizeToTray = _chkMinimizeTray.Checked);

            this.AddButton("📂 配置", P, rY + 54, 90, OpenConfigFolder);
            this.AddButton("📋 日志", 106, rY + 54, 80, OpenLog);
            this.AddButton("ℹ 关于", 192, rY + 54, 70, ShowAbout);
            this.AddButton("💾 保存", 268, rY + 54, 80, SaveSettings);

            for (int i = 0; i < _entries.Count; i++) AddTargetUI(_entries[i]);
            ApplyDarkTheme();
        }

        private static void AddGroup(string text, int x, ref int y, int h, int w, Action<GroupBox> build)
        {
            var gb = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            build(gb);
            Application.OpenForms[0].Controls.Add(gb);
            y += h + 6;
        }

        private CheckBox AddCheck(string text, int x, int y, FontStyle style, bool initial, Action onChange)
        {
            var chk = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Checked = initial };
            if (style != FontStyle.Regular) chk.Font = new Font("Microsoft YaHei UI", 9.5f, style);
            chk.CheckedChanged += (_, _) => onChange();
            Controls.Add(chk);
            return chk;
        }

        // ════════════════════════════════════════════════════════════
        // 主题
        // ════════════════════════════════════════════════════════════

        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.FromArgb(224, 224, 224);
            var panelBg = Color.FromArgb(40, 40, 40);
            BackColor = bg; ForeColor = fg;
            if (IsHandleCreated) { int d = 1; Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4); }
            ThemeAll(this, bg, fg, panelBg);
        }

        private static void ThemeAll(Control parent, Color bg, Color fg, Color panelBg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is GroupBox or Panel or FlowLayoutPanel) { c.BackColor = panelBg; c.ForeColor = fg; }
                else if (c is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = fg; btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (c is CheckBox) { c.BackColor = bg; c.ForeColor = fg; }
                else if (c is Label lbl) { lbl.BackColor = lbl.Parent is Panel or FlowLayoutPanel ? panelBg : bg; if (lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.FromArgb(140, 140, 140); else lbl.ForeColor = fg; }
                else if (c is TrackBar) { c.BackColor = panelBg; }
                else { c.BackColor = bg; c.ForeColor = fg; }
                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        // ════════════════════════════════════════════════════════════
        // UI 同步 + 托盘
        // ════════════════════════════════════════════════════════════

        private void UpdateUI()
        {
            _lblStatus.Text = !_settings.Enabled ? "⏸ 已暂停" : _entries.Count == 0 ? "○ 等待选择窗口…" : $"● 监控中 — {_entries.Count} 个窗口";
            _chkEnabled.Checked = _settings.Enabled;
            if (_tbAlpha.Value != _settings.Alpha) _tbAlpha.Value = _settings.Alpha;
            _lblAlpha.Text = $"{_tbAlpha.Value}%";
            _chkStartup.Checked = _settings.StartWithWindows;
            _chkMinimizeTray.Checked = _settings.MinimizeToTray;
            RefreshTrayMenu();
        }

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "WindowTinter", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            _menu.Items.Add(_lblStatus.Text).Enabled = false; _menu.Items.Add("-");
            _menu.Items.Add(Visible ? "最小化到托盘" : "打开窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => ToggleEnabled());
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (_, _) => { _reallyQuit = true; Close(); });
        }

        private void ToggleWindow() { if (Visible) Hide(); else { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); } }
        private void OnFormClosing(object sender, FormClosingEventArgs e) { if (!_reallyQuit && _settings.MinimizeToTray) { e.Cancel = true; Hide(); } }

        // ════════════════════════════════════════════════════════════
        // 核心操作
        // ════════════════════════════════════════════════════════════

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled) { foreach (var t in _settings.Targets) TryBindTarget(t); foreach (var e in _entries) ShowMask(e); }
            else foreach (var e in _entries) e.Mask.Hide();
            UpdateUI();
        }

        private void SetAlpha(int value)
        {
            _settings.Alpha = Math.Clamp(value, 0, 100);
            foreach (var e in _entries) ShowMask(e);
            _lblAlpha.Text = $"{_tbAlpha.Value}%";
        }

        private void PickWindow()
        {
            bool was = Visible; if (was) Hide();
            using var picker = new WindowPickerForm();
            if (picker.ShowDialog() != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) { if (was) { Show(); BringToFront(); Activate(); } return; }
            if (was) { Show(); BringToFront(); Activate(); }

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            var info = new TargetInfo();
            try { info.ProcessName = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch { }
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0) { var sb = new System.Text.StringBuilder(len + 1); Native.GetWindowText(picker.SelectedHandle, sb, len + 1); info.WindowTitle = sb.ToString(); }
            if (_settings.Targets.Any(t => t.WindowTitle == info.WindowTitle && t.ProcessName == info.ProcessName))
            { MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            _settings.Targets.Add(info);
            if (_settings.Enabled) TryBindTarget(info);
            UpdateUI();
        }

        private void RefindAllWindows() { UnbindAll(); foreach (var t in _settings.Targets) TryBindTarget(t); UpdateUI(); }
        private void SaveSettings() { _settings.Save(); }
        private void OpenConfigFolder() { try { Process.Start("explorer.exe", Path.GetDirectoryName(Environment.ProcessPath)); } catch { } }
        private void OpenLog() { try { var p = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WindowTinter.debug.log"); if (File.Exists(p)) Process.Start("notepad.exe", p); else MessageBox.Show("日志尚不存在。", "WindowTinter"); } catch { } }
        private void ShowAbout() => MessageBox.Show("WindowTinter v2.5\n\nUpdateLayeredWindow 蒙版工具\nWS_EX_TRANSPARENT 点击穿透 · 前景暗化\n\nhttps://github.com/Simiely/WindowTinter", "关于");

        // ════════════════════════════════════════════════════════════
        // WinEvent
        // ════════════════════════════════════════════════════════════

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_ZORDERCHANGES,
                IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            bool global = eventType is Native.EVENT_SYSTEM_FOREGROUND or Native.EVENT_OBJECT_ZORDERCHANGES;
            bool target = !global && _entries.Any(e => e.Tracker.TargetHandle == hwnd)
                && eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY;
            if (global || target) try { BeginInvoke(() => { foreach (var e in _entries) e.Tracker.RefreshNow(); }); } catch { }
        }

        private void Quit()
        {
            _settings.Save(); _reallyQuit = true; _tray.Visible = false;
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            foreach (var e in _entries) { e.Mask.Dispose(); e.Tracker.Dispose(); }
            Application.Exit();
        }

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

    internal static class ControlExt
    {
        public static T AddChild<T>(this Control parent, T child) where T : Control { parent.Controls.Add(child); return child; }
        public static Button AddButton(this Control parent, string text, int x, int y, int w, Action onClick)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30) };
            btn.Click += (_, _) => onClick();
            parent.Controls.Add(btn);
            return btn;
        }
    }
}
