using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WindowTinter
{
    internal class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly TargetTracker _tracker;
        private readonly MaskOverlay _mask;
        private readonly InvertLens _invert;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;

        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private readonly Action _refreshAction;
        private string _targetDisplayName = "";

        // 窗口 UI 控件
        private Label _lblStatus;
        private Label _lblTarget;
        private Button _btnPickWindow;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private RadioButton _rbMask;
        private RadioButton _rbInvert;
        private TrackBar _tbAlpha;
        private Label _lblAlpha;
        private Button _btnPreset50, _btnPreset100, _btnPreset150, _btnPreset200;
        private CheckBox _chkStartup;
        private Button _btnConfigFolder;
        private Button _btnViewLog;
        private Button _btnAbout;

        private bool _reallyQuit;

        public MainForm()
        {
            _settings = Settings.Load();
            _tracker = new TargetTracker();
            _mask = new MaskOverlay();
            _invert = new InvertLens();
            _refreshAction = () => _tracker.RefreshNow();

            Text = "WindowTinter";
            ClientSize = new Size(460, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Load += OnLoad;
            FormClosing += OnFormClosing;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            DebugLog.Info($"WindowTinter 启动 (Alpha={_settings.Alpha}, Mode={_settings.Mode}, Enabled={_settings.Enabled})");

            BuildTray();
            BuildUI();
            _tracker.OnUpdate += OnTargetUpdate;

            // 若已启用且有存储的目标，自动查找
            if (_settings.Enabled && !string.IsNullOrEmpty(_settings.TargetProcessName))
            {
                DebugLog.Info($"自动查找目标窗口: title=\"{_settings.TargetWindowTitle}\", process={_settings.TargetProcessName}");
                var h = TargetTracker.FindByTitleAndProcess(_settings.TargetWindowTitle, _settings.TargetProcessName);
                _tracker.TargetHandle = h;
                if (h != IntPtr.Zero)
                    DebugLog.Info($"目标窗口已找到: hwnd={h}");
                else
                    DebugLog.Info("目标窗口未找到，将持续监听");
                ApplyMode();
                if (_settings.Mode == "Invert") _invert.Start();
            }

            RefreshOwnWindows();
            InstallWinEventHook();
            _tracker.RefreshNow();
            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        // ── Windows 设置界面 ────────────────────────────────────────────

        private void BuildUI()
        {
            int y = 12;
            int pad = 8;

            // ── 状态 ──
            var gbStatus = new GroupBox { Text = "状态", Location = new Point(pad, y), Size = new Size(388, 50) };
            _lblStatus = new Label { Location = new Point(pad, 20), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            gbStatus.Controls.Add(_lblStatus);
            Controls.Add(gbStatus);
            y += 56;

            // ── 目标窗口 ──
            var gbTarget = new GroupBox { Text = "目标窗口", Location = new Point(pad, y), Size = new Size(388, 80) };
            _lblTarget = new Label { Location = new Point(pad, 22), AutoSize = true, Text = "未绑定" };
            gbTarget.Controls.Add(_lblTarget);
            _btnPickWindow = new Button { Text = "⊕ 选择窗口", Location = new Point(pad, 44), Size = new Size(90, 26) };
            _btnPickWindow.Click += (s, ev) => PickWindow();
            gbTarget.Controls.Add(_btnPickWindow);
            _btnRefind = new Button { Text = "🔄 重新查找", Location = new Point(102, 44), Size = new Size(90, 26) };
            _btnRefind.Click += (s, ev) => RefindWindow();
            gbTarget.Controls.Add(_btnRefind);
            Controls.Add(gbTarget);
            y += 86;

            // ── 启用 / 停用 ──
            _chkEnabled = new CheckBox
            {
                Text = "启用覆盖",
                Location = new Point(pad + 4, y),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Checked = _settings.Enabled
            };
            _chkEnabled.CheckedChanged += (s, ev) => ToggleEnabled();
            Controls.Add(_chkEnabled);
            y += 28;

            // ── 模式 ──
            var gbMode = new GroupBox { Text = "模式", Location = new Point(pad, y), Size = new Size(388, 50) };
            _rbMask = new RadioButton { Text = "深色蒙版", Location = new Point(pad, 20), AutoSize = true, Checked = _settings.Mode == "Mask" };
            _rbMask.CheckedChanged += (s, ev) => { if (_rbMask.Checked) SetMode("Mask"); };
            _rbInvert = new RadioButton { Text = "真·反色 (实验)", Location = new Point(200, 20), AutoSize = true, Checked = _settings.Mode == "Invert" };
            _rbInvert.CheckedChanged += (s, ev) => { if (_rbInvert.Checked) SetMode("Invert"); };
            gbMode.Controls.Add(_rbMask);
            gbMode.Controls.Add(_rbInvert);
            Controls.Add(gbMode);
            y += 56;

            // ── 透明度 ──
            var gbAlpha = new GroupBox { Text = "透明度", Location = new Point(pad, y), Size = new Size(388, 82) };
            _tbAlpha = new TrackBar
            {
                Location = new Point(pad, 18), Size = new Size(280, 40),
                Minimum = 10, Maximum = 255, Value = _settings.Alpha,
                TickFrequency = 25, SmallChange = 10, LargeChange = 50
            };
            _tbAlpha.ValueChanged += (s, ev) => SetAlpha(_tbAlpha.Value);
            gbAlpha.Controls.Add(_tbAlpha);
            _lblAlpha = new Label { Location = new Point(296, 22), AutoSize = true, Width = 60 };
            gbAlpha.Controls.Add(_lblAlpha);

            // 预设按钮
            var presets = new[] { ("轻", 50, 316, 48), ("中", 100, 350, 48), ("重", 150, 316, 48+24), ("极暗", 200, 350, 48+24) };
            foreach (var (label, val, px, py) in presets)
            {
                var btn = new Button { Text = label, Size = new Size(30, 22), Location = new Point(px, py), FlatStyle = FlatStyle.Flat };
                int v = val;
                btn.Click += (s, ev) => SetAlpha(v);
                gbAlpha.Controls.Add(btn);
                if (val == 50) _btnPreset50 = btn; else if (val == 100) _btnPreset100 = btn;
                else if (val == 150) _btnPreset150 = btn; else _btnPreset200 = btn;
            }
            Controls.Add(gbAlpha);
            y += 88;

            // ── 系统 ──
            var rowY = y + 2;
            _chkStartup = new CheckBox { Text = "开机自启", Location = new Point(pad + 4, rowY), AutoSize = true, Checked = _settings.StartWithWindows };
            _chkStartup.CheckedChanged += (s, ev) =>
            {
                _settings.StartWithWindows = _chkStartup.Checked;
                _settings.ApplyStartWithWindows();
                _settings.Save();
            };
            Controls.Add(_chkStartup);
            rowY += 30;

            _btnConfigFolder = new Button { Text = "📂 配置文件夹", Location = new Point(pad, rowY), Size = new Size(110, 28) };
            _btnConfigFolder.Click += (s, ev) => OpenConfigFolder();
            Controls.Add(_btnConfigFolder);
            _btnViewLog = new Button { Text = "📋 查看日志", Location = new Point(122, rowY), Size = new Size(90, 28) };
            _btnViewLog.Click += (s, ev) => OpenLog();
            Controls.Add(_btnViewLog);
            _btnAbout = new Button { Text = "ℹ 关于", Location = new Point(220, rowY), Size = new Size(60, 28) };
            _btnAbout.Click += (s, ev) => ShowAbout();
            Controls.Add(_btnAbout);
            y += 64;

            // ── 提示 ──
            var hint = new Label
            {
                Text = "关闭窗口将最小化到系统托盘",
                Location = new Point(pad, rowY + 36),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            Controls.Add(hint);

            ApplyDarkTheme();
        }

        /// <summary>深色主题：背景 #1E1E1E，文字 #E0E0E0，标题栏也暗。</summary>
        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);       // #1E1E1E
            var fg = Color.FromArgb(224, 224, 224);    // #E0E0E0
            var panelBg = Color.FromArgb(40, 40, 40);

            BackColor = bg;
            ForeColor = fg;

            // 标题栏深色 (Win10 2004+)
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
                if (c is GroupBox || c is Panel)
                {
                    c.BackColor = panelBg;
                    c.ForeColor = fg;
                }
                else if (c is Button btn)
                {
                    btn.BackColor = Color.FromArgb(60, 60, 60);
                    btn.ForeColor = fg;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                }
                else if (c is CheckBox || c is RadioButton)
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }
                else if (c is TrackBar)
                {
                    c.BackColor = panelBg;
                }
                else if (c is Label)
                {
                    c.BackColor = bg;
                    c.ForeColor = c.ForeColor == Color.Gray ? Color.FromArgb(140, 140, 140) : fg;
                }
                else
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }

                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        private void UpdateUI()
        {
            // 状态
            if (!_settings.Enabled)
                _lblStatus.Text = "⏸ 已暂停";
            else if (_tracker.TargetHandle == IntPtr.Zero)
                _lblStatus.Text = "○ 等待选择窗口…";
            else
                _lblStatus.Text = $"● 监控中 — {_targetDisplayName}";

            // 目标
            _lblTarget.Text = _tracker.TargetHandle != IntPtr.Zero
                ? $"已绑定: {_targetDisplayName}" : "未绑定 — 请点击「选择窗口」";

            // 控件同步
            _chkEnabled.Checked = _settings.Enabled;
            _rbMask.Checked = _settings.Mode == "Mask";
            _rbInvert.Checked = _settings.Mode == "Invert";
            if (_tbAlpha.Value != _settings.Alpha) _tbAlpha.Value = _settings.Alpha;
            _lblAlpha.Text = $"{_settings.Alpha}/255";
            _chkStartup.Checked = _settings.StartWithWindows;
        }

        // ── 托盘菜单（精简版） ──────────────────────────────────────────

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            RefreshTrayMenu();
            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "WindowTinter",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _tray.DoubleClick += (s, e) => ToggleWindow();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();

            string status;
            if (!_settings.Enabled) status = "⏸ 已暂停";
            else if (_tracker.TargetHandle == IntPtr.Zero) status = "○ 等待选择窗口…";
            else status = $"● 监控中 — {_targetDisplayName}";
            _menu.Items.Add(status).Enabled = false;
            _menu.Items.Add("-");

            _menu.Items.Add(Visible ? "最小化到托盘" : "打开设置窗口", null, (s, e) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (s, e) => ToggleEnabled());
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (s, e) => { _reallyQuit = true; Close(); });
        }

        private void ToggleWindow()
        {
            if (Visible)
                Hide();
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyQuit)
            {
                e.Cancel = true;
                Hide();
            }
        }

        // ── 核心逻辑（不变） ────────────────────────────────────────────

        private void RefreshOwnWindows()
        {
            var own = new List<IntPtr>(4) { _mask.Handle };
            foreach (var h in _invert.OwnHandles) own.Add(h);
            _tracker.OwnWindows = own.ToArray();
        }

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND,
                Native.EVENT_OBJECT_ZORDERCHANGES,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            bool relevant;
            switch (eventType)
            {
                case Native.EVENT_SYSTEM_FOREGROUND:
                case Native.EVENT_OBJECT_ZORDERCHANGES:
                    relevant = true; break;
                case Native.EVENT_OBJECT_LOCATIONCHANGE:
                case Native.EVENT_OBJECT_HIDE:
                case Native.EVENT_OBJECT_SHOW:
                case Native.EVENT_OBJECT_DESTROY:
                    relevant = (hwnd == _tracker.TargetHandle); break;
                default: relevant = false; break;
            }
            if (relevant) { try { BeginInvoke(_refreshAction); } catch { } }
        }

        private void OnTargetUpdate(Native.RECT r, bool visible, IntPtr hrgn, bool occluded)
        {
            try
            {
                if (hrgn != IntPtr.Zero) Native.DeleteObject(hrgn);

                if (!_settings.Enabled || _tracker.TargetHandle == IntPtr.Zero || !visible)
                {
                    _mask.Hide(); _invert.Hide(); return;
                }
                if (_settings.Mode == "Invert")
                {
                    _mask.Hide(); _invert.Update(r, IntPtr.Zero);
                }
                else
                {
                    _invert.Hide();
                    _mask.Alpha = (byte)_settings.Alpha;
                    _mask.AlignTo(r);
                }
            }
            catch (Exception ex) { DebugLog.Error("OnTargetUpdate 异常", ex); }
        }

        private void ApplyMode() => _mask.Alpha = (byte)_settings.Alpha;

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled)
            {
                if (_tracker.TargetHandle == IntPtr.Zero && !string.IsNullOrEmpty(_settings.TargetProcessName))
                {
                    var h = TargetTracker.FindByTitleAndProcess(_settings.TargetWindowTitle, _settings.TargetProcessName);
                    _tracker.TargetHandle = h;
                }
            }
            else { _mask.Hide(); _invert.Hide(); }
            _settings.Save();
            RefreshTrayMenu(); UpdateUI();
        }

        private void SetMode(string mode)
        {
            _settings.Mode = mode;
            if (mode == "Invert") { _invert.Start(); RefreshOwnWindows(); }
            else _invert.Hide();
            ApplyMode();
            _settings.Save();
            RefreshTrayMenu(); UpdateUI();
        }

        private void SetAlpha(int value)
        {
            _settings.Alpha = Math.Max(10, Math.Min(255, value));
            _mask.Alpha = (byte)_settings.Alpha;
            _settings.Save();
            if (_tbAlpha.Value != _settings.Alpha) _tbAlpha.Value = _settings.Alpha;
            _lblAlpha.Text = $"{_settings.Alpha}/255";
            RefreshTrayMenu();
        }

        private void PickWindow()
        {
            // 先最小化自己，避免拾取到自己
            bool wasVisible = Visible;
            if (wasVisible) Hide();

            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();

            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result == DialogResult.OK && picker.SelectedHandle != IntPtr.Zero)
            {
                _tracker.TargetHandle = picker.SelectedHandle;
                uint pid;
                Native.GetWindowThreadProcessId(picker.SelectedHandle, out pid);
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    _settings.TargetProcessName = proc.ProcessName + ".exe";
                    _targetDisplayName = proc.ProcessName;
                }
                catch { _settings.TargetProcessName = ""; _targetDisplayName = "未知"; }
                int len = Native.GetWindowTextLength(picker.SelectedHandle);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                    _settings.TargetWindowTitle = sb.ToString();
                    if (string.IsNullOrEmpty(_targetDisplayName)) _targetDisplayName = sb.ToString();
                }
                _settings.Save();
                DebugLog.Info($"已选择窗口: title=\"{_settings.TargetWindowTitle}\", process={_settings.TargetProcessName}");
            }
            RefreshTrayMenu(); UpdateUI();
        }

        private void RefindWindow()
        {
            var h = TargetTracker.FindByTitleAndProcess(_settings.TargetWindowTitle, _settings.TargetProcessName);
            _tracker.TargetHandle = h;
            DebugLog.Info($"重新查找窗口: {(h != IntPtr.Zero ? "成功" : "未找到")}");
            if (h == IntPtr.Zero)
                MessageBox.Show("未找到目标窗口，请确认窗口已打开。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshTrayMenu(); UpdateUI();
        }

        private void OpenConfigFolder()
        {
            try
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                Process.Start("explorer.exe", dir);
            }
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
                "WindowTinter v2.1.2\n\n" +
                "给任意窗口叠加深色半透明蒙版的 Windows 常驻小工具。\n\n" +
                "• 蒙版：UpdateLayeredWindow 逐像素合成\n" +
                "• 拾取：全屏超低透明度捕获层 + 反转边框\n" +
                "• 配置：exe 同目录 WindowTinter.settings.json\n" +
                "• 日志：exe 同目录 WindowTinter.debug.log\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Quit()
        {
            DebugLog.Info("WindowTinter 退出");
            _reallyQuit = true;
            _tray.Visible = false;
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            _invert.Dispose();
            _mask.Dispose();
            _tracker.Dispose();
            Application.Exit();
        }

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
