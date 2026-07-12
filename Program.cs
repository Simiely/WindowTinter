using System;
using System.Collections.Generic;
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

        // 事件驱动更新（替代高频轮询）：仅在窗口移动/置顶/前台切换时刷新
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private readonly Action _refreshAction;

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
            Shown += (s, e) => Hide(); // 隐藏控制器窗口，仅留托盘
        }

        private void OnLoad(object sender, EventArgs e)
        {
            BuildTray();
            _tracker.OnUpdate += OnTargetUpdate;

            // 若上次启用，自动寻找目标
            if (_settings.Enabled)
            {
                var h = TargetTracker.FindByProcessName(_settings.TargetProcessName);
                _tracker.TargetHandle = h;
                ApplyMode();
                if (_settings.Mode == "Invert") _invert.Start();
            }

            // 告知 Tracker 哪些窗口是“自己人”，遮挡遍历时跳过，避免误判自遮挡
            RefreshOwnWindows();
            InstallWinEventHook();
            _tracker.RefreshNow(); // 立即定位一次，避免 startup 等待首个定时器

            _settings.ApplyStartWithWindows();
        }

        /// <summary>收集本工具自身窗口句柄（蒙版 + 放大镜宿主/控件），供遮挡检测跳过。</summary>
        private void RefreshOwnWindows()
        {
            var own = new List<IntPtr>(4) { _mask.Handle };
            foreach (var h in _invert.OwnHandles) own.Add(h);
            _tracker.OwnWindows = own.ToArray();
        }

        /// <summary>
        /// 安装 WinEvent 钩子：监听窗口移动/缩放/显示隐藏/销毁/Z 序变化/前台切换，
        /// 事件驱动刷新（带变更守卫），取代每 100ms 的无脑轮询，消除卡顿。
        /// </summary>
        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND,   // 0x0003
                Native.EVENT_OBJECT_ZORDERCHANGES, // 0x8012（覆盖到 0x8001~0x8012 全部 OBJECT 事件）
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return; // 只关心窗口本身，忽略子对象
            bool relevant;
            switch (eventType)
            {
                case Native.EVENT_SYSTEM_FOREGROUND:
                case Native.EVENT_OBJECT_ZORDERCHANGES:
                    relevant = true; // 前台/Z 序变化→遮挡可能变，无条件刷新
                    break;
                case Native.EVENT_OBJECT_LOCATIONCHANGE:
                case Native.EVENT_OBJECT_HIDE:
                case Native.EVENT_OBJECT_SHOW:
                case Native.EVENT_OBJECT_DESTROY:
                    relevant = (hwnd == _tracker.TargetHandle); // 仅目标自身的几何/显隐变化
                    break;
                default:
                    relevant = false;
                    break;
            }
            if (relevant) BeginInvoke(_refreshAction); // 回到 UI 线程刷新
        }

        // 每帧：根据模式、可见性、遮挡状态，定位/隐藏覆盖层
        private void OnTargetUpdate(Native.RECT r, bool visible, IntPtr hrgn, bool occluded)
        {
            bool ownsRegion = false; // 蒙版或反色镜头接管 hrgn 时为 true
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
                    _invert.Update(r, hrgn); // 反色：接管 hrgn，按可见区域裁剪（只反色百度可见部分）
                    ownsRegion = true;
                }
                else
                {
                    _invert.Hide();
                    _mask.AlignTo(r, hrgn); // 蒙版：接管 hrgn 所有权，按可见区域裁剪
                    ownsRegion = true;
                }
            }
            finally
            {
                // 未被任何覆盖层接管的区域必须在此释放，避免 GDI 对象泄漏
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
            _tray.Click += (s, e) => _menu.Show(Cursor.Position);   // 左键也弹出菜单（右键由 ContextMenuStrip 自动弹出）
            _tray.DoubleClick += (s, e) => ToggleEnabled();
        }

        private void RefreshMenu()
        {
            _menu.Items.Clear();
            _menu.Items.Add(_settings.Enabled ? "停用 (隐藏覆盖)" : "启用覆盖", null, (s, e) => ToggleEnabled());
            _menu.Items.Add("── 模式 ──").Enabled = false;
            ((ToolStripMenuItem)_menu.Items.Add("深色蒙版", null, (s, e) => SetMode("Mask"))).Checked = _settings.Mode == "Mask";
            ((ToolStripMenuItem)_menu.Items.Add("真·反色 (实验)", null, (s, e) => SetMode("Invert"))).Checked = _settings.Mode == "Invert";
            _menu.Items.Add("── 透明度 ──").Enabled = false;
            _menu.Items.Add("调暗一点 (-)", null, (s, e) => ChangeAlpha(-20));
            _menu.Items.Add("调亮一点 (+)", null, (s, e) => ChangeAlpha(+20));
            _menu.Items.Add($"当前不透明度: {_settings.Alpha}/255");
            _menu.Items.Add("── 目标窗口 ──").Enabled = false;
            _menu.Items.Add("选择窗口 (点击拾取)...", null, (s, e) => PickWindow());
            _menu.Items.Add("重新绑定百度网盘", null, (s, e) => BindBaidu());
            _menu.Items.Add($"当前目标: {(_tracker.TargetHandle != IntPtr.Zero ? "已绑定" : "未绑定")}");
            ((ToolStripMenuItem)_menu.Items.Add("开机自启", null, (s, e) =>
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                _settings.ApplyStartWithWindows();
                _settings.Save();
                RefreshMenu();
            })).Checked = _settings.StartWithWindows;
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
            if (mode == "Invert") { _invert.Start(); RefreshOwnWindows(); } // 懒初始化放大镜，并纳入遮挡跳过集合
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
            // 百度网盘不同版本进程名不同，逐个尝试
            string[] baiduProcessNames = { "BaiduNetdiskUnite.exe", "BaiduNetdisk.exe" };
            IntPtr h = IntPtr.Zero;
            string matchedName = "";
            foreach (var name in baiduProcessNames)
            {
                h = TargetTracker.FindByProcessName(name);
                if (h != IntPtr.Zero) { matchedName = name; break; }
            }

            // 进程名没找到？按窗口标题兜底
            if (h == IntPtr.Zero)
                h = TargetTracker.FindByWindowTitle("百度网盘");

            if (h != IntPtr.Zero)
            {
                _tracker.TargetHandle = h;
                _settings.TargetProcessName = string.IsNullOrEmpty(matchedName) ? "BaiduNetdiskUnite.exe" : matchedName;
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
            Application.Run(new MainForm());
        }
    }
}
