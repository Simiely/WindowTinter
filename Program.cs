using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 主控制器：隐藏窗体 + 托盘图标 + 模式切换 + 目标跟踪。
    /// </summary>
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

        // 目标进程显示名（用于托盘状态文字）
        private string _targetDisplayName = "";

        public MainForm()
        {
            _settings = Settings.Load();
            _tracker = new TargetTracker();
            _mask = new MaskOverlay();
            _invert = new InvertLens();
            _refreshAction = () => _tracker.RefreshNow();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Load += OnLoad;
            Shown += (s, e) => Hide();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            DebugLog.Info($"WindowTinter 启动 (Alpha={_settings.Alpha}, Mode={_settings.Mode}, Enabled={_settings.Enabled})");

            BuildTray();
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
                    DebugLog.Info("目标窗口未找到（可能尚未打开），将持续监听");
                ApplyMode();
                if (_settings.Mode == "Invert") _invert.Start();
            }

            RefreshOwnWindows();
            InstallWinEventHook();
            _tracker.RefreshNow();
            _settings.ApplyStartWithWindows();
        }

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

        private void WinEventProcCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            bool relevant;
            switch (eventType)
            {
                case Native.EVENT_SYSTEM_FOREGROUND:
                case Native.EVENT_OBJECT_ZORDERCHANGES:
                    relevant = true;
                    break;
                case Native.EVENT_OBJECT_LOCATIONCHANGE:
                case Native.EVENT_OBJECT_HIDE:
                case Native.EVENT_OBJECT_SHOW:
                case Native.EVENT_OBJECT_DESTROY:
                    relevant = (hwnd == _tracker.TargetHandle);
                    break;
                default:
                    relevant = false;
                    break;
            }
            if (relevant)
            {
                try { BeginInvoke(_refreshAction); }
                catch { /* 窗口可能正在关闭 */ }
            }
        }

        private void OnTargetUpdate(Native.RECT r, bool visible, IntPtr hrgn, bool occluded)
        {
            bool ownsRegion = false;
            try
            {
                if (!_settings.Enabled || _tracker.TargetHandle == IntPtr.Zero || !visible)
                {
                    _mask.Hide();
                    _invert.Hide();
                    return;
                }

                if (_settings.Mode == "Invert")
                {
                    _mask.Hide();
                    _invert.Update(r, hrgn);
                    ownsRegion = true;
                }
                else
                {
                    _invert.Hide();
                    _mask.Alpha = (byte)_settings.Alpha;
                    _mask.AlignTo(r, hrgn);
                    ownsRegion = true;
                }
            }
            finally
            {
                if (!ownsRegion && hrgn != IntPtr.Zero)
                    Native.DeleteObject(hrgn);
            }
        }

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            RefreshMenu();
            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "WindowTinter",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _tray.Click += (s, e) => _menu.Show(Cursor.Position);
            _tray.DoubleClick += (s, e) => ToggleEnabled();
        }

        private void RefreshMenu()
        {
            _menu.Items.Clear();

            // 状态行
            string status;
            if (!_settings.Enabled)
                status = "○ 已暂停";
            else if (_tracker.TargetHandle == IntPtr.Zero)
                status = "○ 等待选择窗口…";
            else
                status = $"● 监控中 — {_targetDisplayName}";
            _menu.Items.Add(status).Enabled = false;

            _menu.Items.Add("-");
            _menu.Items.Add(_settings.Enabled ? "停用 (隐藏覆盖)" : "启用覆盖", null, (s, e) => ToggleEnabled());
            _menu.Items.Add("── 模式 ──").Enabled = false;
            ((ToolStripMenuItem)_menu.Items.Add("深色蒙版", null, (s, e) => SetMode("Mask"))).Checked = _settings.Mode == "Mask";
            ((ToolStripMenuItem)_menu.Items.Add("真·反色 (实验)", null, (s, e) => SetMode("Invert"))).Checked = _settings.Mode == "Invert";
            _menu.Items.Add("── 透明度 ──").Enabled = false;
            _menu.Items.Add("调暗一点 (-)", null, (s, e) => ChangeAlpha(-20));
            _menu.Items.Add("调亮一点 (+)", null, (s, e) => ChangeAlpha(+20));
            _menu.Items.Add($"当前不透明度: {_settings.Alpha}/255");
            _menu.Items.Add("── 目标窗口 ──").Enabled = false;
            _menu.Items.Add("选择窗口 (拖拽拾取)...", null, (s, e) => PickWindow());
            _menu.Items.Add($"当前目标: {(_tracker.TargetHandle != IntPtr.Zero ? _targetDisplayName : "未绑定")}");
            ((ToolStripMenuItem)_menu.Items.Add("开机自启", null, (s, e) =>
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                _settings.ApplyStartWithWindows();
                _settings.Save();
                RefreshMenu();
            })).Checked = _settings.StartWithWindows;
            _menu.Items.Add("── 调试 ──").Enabled = false;
            _menu.Items.Add("查看日志", null, (s, e) => OpenLog());
            _menu.Items.Add("退出", null, (s, e) => Quit());
        }

        private void ApplyMode()
        {
            _mask.Alpha = (byte)_settings.Alpha;
        }

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled)
            {
                // 启用时自动查找目标窗口
                if (_tracker.TargetHandle == IntPtr.Zero && !string.IsNullOrEmpty(_settings.TargetProcessName))
                {
                    var h = TargetTracker.FindByTitleAndProcess(_settings.TargetWindowTitle, _settings.TargetProcessName);
                    _tracker.TargetHandle = h;
                    DebugLog.Info($"启用时查找目标: {(h != IntPtr.Zero ? "成功" : "未找到，将持续监听")}");
                }
            }
            else
            {
                _mask.Hide();
                _invert.Hide();
            }
            _settings.Save();
            RefreshMenu();
        }

        private void SetMode(string mode)
        {
            _settings.Mode = mode;
            if (mode == "Invert") { _invert.Start(); RefreshOwnWindows(); }
            else _invert.Hide();
            ApplyMode();
            _settings.Save();
            RefreshMenu();
        }

        private void ChangeAlpha(int delta)
        {
            int a = _settings.Alpha + delta;
            _settings.Alpha = Math.Max(10, Math.Min(255, a));
            _mask.Alpha = (byte)_settings.Alpha;
            _settings.Save();
            RefreshMenu();
        }

        private void PickWindow()
        {
            using var picker = new WindowPickerForm();
            if (picker.ShowDialog() == DialogResult.OK && picker.SelectedHandle != IntPtr.Zero)
            {
                _tracker.TargetHandle = picker.SelectedHandle;
                // 记下进程名和标题以便下次自动绑定
                uint pid;
                Native.GetWindowThreadProcessId(picker.SelectedHandle, out pid);
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    _settings.TargetProcessName = proc.ProcessName + ".exe";
                    _targetDisplayName = proc.ProcessName;
                }
                catch { _settings.TargetProcessName = ""; _targetDisplayName = "未知"; }

                // 记下窗口标题
                int len = Native.GetWindowTextLength(picker.SelectedHandle);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                    _settings.TargetWindowTitle = sb.ToString();
                    if (string.IsNullOrEmpty(_targetDisplayName))
                        _targetDisplayName = sb.ToString();
                }

                _settings.Save();
                DebugLog.Info($"已选择窗口: title=\"{_settings.TargetWindowTitle}\", process={_settings.TargetProcessName}");
                RefreshMenu();
            }
        }

        private void OpenLog()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowTinter", "debug.log");
                if (System.IO.File.Exists(path))
                    Process.Start("notepad.exe", path);
                else
                    MessageBox.Show("日志文件尚不存在，程序运行后会自动生成。", "WindowTinter");
            }
            catch (Exception ex) { DebugLog.Error("打开日志失败", ex); }
        }

        private void Quit()
        {
            DebugLog.Info("WindowTinter 退出");
            _tray.Visible = false;
            if (_winEventHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
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

            // 全局异常捕获：崩溃时写入日志
            Application.ThreadException += (s, e) =>
                DebugLog.Error("UI线程异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                DebugLog.Error("未处理异常", e.ExceptionObject as Exception);

            Application.Run(new MainForm());
        }
    }
}
