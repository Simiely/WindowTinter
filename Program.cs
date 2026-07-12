using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 主控制器：隐藏窗体 + 托盘图标 + 全局热键 + 模式切换 + 目标跟踪。
    /// </summary>
    internal class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly TargetTracker _tracker;
        private readonly MaskOverlay _mask;
        private readonly InvertLens _invert;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;

        public MainForm()
        {
            _settings = Settings.Load();
            _tracker = new TargetTracker();
            _mask = new MaskOverlay();
            _invert = new InvertLens();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Load += OnLoad;
            Shown += (s, e) => Hide(); // 隐藏控制器窗口，仅留托盘
        }

        private void OnLoad(object sender, EventArgs e)
        {
            BuildTray();
            _tracker.OnUpdate += OnTargetUpdate;

            // 注册全局热键
            Hotkey.Register(Handle, _settings.HotkeyModifiers, _settings.HotkeyVk);

            // 若上次启用，自动寻找目标
            if (_settings.Enabled)
            {
                var h = TargetTracker.FindByProcessName(_settings.TargetProcessName);
                _tracker.TargetHandle = h;
                ApplyMode();
                if (_settings.Mode == "Invert") _invert.Start();
            }

            _settings.ApplyStartWithWindows();
        }

        // 每帧：根据模式与目标可见性，定位/隐藏覆盖层
        private void OnTargetUpdate(Native.RECT r, bool visible)
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
                _invert.Update(r);
            }
            else
            {
                _invert.Hide();
                _mask.AlignTo(r);
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
            _tray.DoubleClick += (s, e) => ToggleEnabled();
        }

        private void RefreshMenu()
        {
            _menu.Items.Clear();
            _menu.Items.Add(_settings.Enabled ? "停用 (隐藏覆盖)" : "启用覆盖", null, (s, e) => ToggleEnabled());
            _menu.Items.Add("── 模式 ──").Enabled = false;
            _menu.Items.Add("深色蒙版", null, (s, e) => SetMode("Mask")).Checked = _settings.Mode == "Mask";
            _menu.Items.Add("真·反色 (实验)", null, (s, e) => SetMode("Invert")).Checked = _settings.Mode == "Invert";
            _menu.Items.Add("── 透明度 ──").Enabled = false;
            _menu.Items.Add("调暗一点 (-)", null, (s, e) => ChangeAlpha(-20));
            _menu.Items.Add("调亮一点 (+)", null, (s, e) => ChangeAlpha(+20));
            _menu.Items.Add($"当前不透明度: {_settings.Alpha}/255");
            _menu.Items.Add("── 目标窗口 ──").Enabled = false;
            _menu.Items.Add("选择窗口 (点击拾取)...", null, (s, e) => PickWindow());
            _menu.Items.Add("重新绑定百度网盘", null, (s, e) => BindBaidu());
            _menu.Items.Add($"当前目标: {(_tracker.TargetHandle != IntPtr.Zero ? "已绑定" : "未绑定")}");
            _menu.Items.Add("开机自启", null, (s, e) =>
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                _settings.ApplyStartWithWindows();
                _settings.Save();
                RefreshMenu();
            }).Checked = _settings.StartWithWindows;
            _menu.Items.Add("退出", null, (s, e) => Quit());
        }

        private void ApplyMode()
        {
            _mask.Alpha = (byte)_settings.Alpha;
        }

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            if (_settings.Enabled && _tracker.TargetHandle == IntPtr.Zero)
            {
                _tracker.TargetHandle = TargetTracker.FindByProcessName(_settings.TargetProcessName);
            }
            _settings.Save();
            RefreshMenu();
        }

        private void SetMode(string mode)
        {
            _settings.Mode = mode;
            if (mode == "Invert") _invert.Start(); // 懒初始化放大镜
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
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedHandle != IntPtr.Zero)
            {
                _tracker.TargetHandle = picker.SelectedHandle;
                // 记下进程名以便下次自动绑定
                uint pid;
                Native.GetWindowThreadProcessId(picker.SelectedHandle, out pid);
                try { _settings.TargetProcessName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + ".exe"; }
                catch { }
                _settings.Save();
                RefreshMenu();
            }
        }

        private void BindBaidu()
        {
            var h = TargetTracker.FindByProcessName("BaiduNetdisk.exe");
            if (h != IntPtr.Zero)
            {
                _tracker.TargetHandle = h;
                _settings.TargetProcessName = "BaiduNetdisk.exe";
                _settings.Save();
            }
            else
            {
                MessageBox.Show("未找到百度网盘窗口，请先打开百度网盘客户端。",
                    "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            RefreshMenu();
        }

        private void Quit()
        {
            _tray.Visible = false;
            Hotkey.Unregister(Handle);
            _invert.Dispose();
            _mask.Dispose();
            _tracker.Dispose();
            Application.Exit();
        }

        // 捕获全局热键
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
                ToggleEnabled();
            base.WndProc(ref m);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
