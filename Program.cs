using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    internal partial class MainForm : Form
    {
        // ── 核心状态 ──────────────────────────────────────────────

        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, Panel> _pendingPanels = new();
        private readonly Dictionary<TargetInfo, Button> _selectButtons = new();
        private TargetInfo _selectedTarget;   // 非全局模式下，滑块当前编辑的目标

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;
        private Timer _onShownTimer;
        private Timer _saveDebounceTimer;
        private Icon _appIcon;
        private float _dpiScale = 1f;   // 系统 DPI 缩放比（DeviceDpi/96）；用于手动换算运行时动态添加的面板尺寸
        private bool _dpiReady;         // OnLoad 后置 true：此后 WM_DPICHANGED 才做 UI 自适应（防初始双重缩放）

        private static readonly string AppVersion = GetAppVersion();
        private static string GetAppVersion()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.5.2";
        }

        // ── UI 控件 ────────────────────────────────────────────────

        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbBgAlpha;
        private Label _lblBgAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkBackdropPlate;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkGlobalTransparency;
        private CheckBox _chkGlobalCornerRadius;
        private TrackBar _tbCornerRadius;
        private Label _lblCornerRadius;

        // ── 窗口 ───────────────────────────────────────────────────

        public MainForm(bool startHidden = false)
        {
            // Win11 高 DPI：以 96 DPI 为设计基准，运行时由 AutoScaleMode.Dpi 按屏幕真实 DPI 自动缩放全部静态控件。
            // 关键：控件必须在 CreateControl（即本构造函数）阶段建好，自动缩放才会作用于它们——
            // 因此 BuildUI() 放在这里而非 OnLoad。BlackPlate 是物理像素分层窗，单独用 AutoScaleMode.None，不受影响。
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            _settings = Settings.Load();

            Text = $"暗幕 v{AppVersion}";
            var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
            try { _appIcon = File.Exists(iconPath) ? new Icon(iconPath) : null; }
            catch { _appIcon = null; }
            Icon = _appIcon; // 图标缺失/损坏时退化为系统默认图标，避免启动崩溃
            ClientSize = new Size(470, 695);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // 开机自启：直接最小化到托盘（WindowState 在句柄创建前设置，窗口以最小化态创建，无闪烁），
            // 且不显示任务栏按钮——仅托盘图标驻留。用户双击托盘即可打开设置窗口。
            if (startHidden)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
            Load += OnLoad;
            Shown += OnShown;
            Activated += OnActivated;
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => Quit();

            BuildUI();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            // 句柄已创建，DeviceDpi 为真实屏幕 DPI（进程已 PerMonitorV2 感知）。
            // 静态控件已由 AutoScaleMode.Dpi 自动缩放；运行时动态添加的面板按此因子换算尺寸。
            _dpiScale = Math.Max(DeviceDpi / 96f, 1f);
            // 关键：_dpiScale 现在才拿到真实值（BuildUI 时为 1.0），立即重建一次列表，
            // 让动态面板宽度按真实 DPI 统一（否则列表项先按 1.0 创建、启动后再重建会宽度跳变）。
            RebuildTargetList();
            _dpiReady = true; // 初始缩放已完成（AutoScaleMode.Dpi），此后 WM_DPICHANGED 才触发 UI 自适应

            // 启动时清除上次强制退出可能残留的透明效果
            RestoreAllTargets();

            BuildTray();
            InstallWinEventHook();

            // 兜底定时器（3s）：只做"尝试绑定未绑定目标"；条目清理由 WinEvent 事件驱动（见 WinEventProcCallback）
            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;
                bool anyBound = false;
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    if (BindTarget(t, refreshUI: false)) anyBound = true;
                }
                if (anyBound) RebuildTargetList(); // 批量：有新增绑定才统一重建一次
            };
            _autoBindTimer.Start();

            // 滑块去抖：拖动停止 200ms 后写盘
            _saveDebounceTimer = new Timer { Interval = 200 };
            _saveDebounceTimer.Tick += (_, _) => { _saveDebounceTimer.Stop(); _settings.Save(); };

            if (_settings.Enabled)
            {
                // 批量启动绑定：不逐次重建 UI，最后统一 Rebuild 一次呈现最终状态
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    BindTarget(t, refreshUI: false);
                }
                RebuildTargetList();
            }

            // 非全局模式下，默认选中第一个目标
            if (!_settings.GlobalTransparency && _selectedTarget == null)
                _selectedTarget = _settings.Targets.FirstOrDefault();

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        private void OnShown(object _, EventArgs __)
        {
            foreach (var e in _entries) e.Tracker.RefreshNow();
            // 用一次性定时器推迟 100ms，确保消息泵完整运转后 UpdateLayeredWindow 稳定
            _onShownTimer = new Timer { Interval = 100 };
            _onShownTimer.Tick += (s, args) => { _onShownTimer.Stop(); _onShownTimer.Dispose(); foreach (var e in _entries) ApplyMaskNow(e); };
            _onShownTimer.Start();
        }

        /// <summary>点击 WindowTinter 自身时 WinEvent SKIPOWNPROCESS 会跳过前台事件，
        /// 通过 Activated 补发 RefreshForeground 让目标切到透明状态。
        /// _inActivated 防 BeginInvoke 嵌套重入（Activated 可能连续触发）。</summary>
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

        /// <summary>启动时遍历所有已配置目标，恢复透明度——处理上次强制杀进程残留。</summary>
        private void RestoreAllTargets()
        {
            foreach (var t in _settings.Targets)
            {
                var h = TargetTracker.FindByTitleAndProcess(t.WindowTitle, t.ProcessName, null, t.WindowClass);
                if (h != IntPtr.Zero)
                {
                    SetTargetAlpha(h, 255, true); // 启动恢复：保留 layered 分支，不破坏原生分层窗口
                    SetTargetTopmost(h, false); // 清理上次强制退出可能残留的置顶
                }
            }
        }

        private void Quit()
        {
            _settings.Save();
            _reallyQuit = true;
            _autoBindTimer?.Stop(); _autoBindTimer?.Dispose();
            _onShownTimer?.Stop(); _onShownTimer?.Dispose();
            _saveDebounceTimer?.Stop(); _saveDebounceTimer?.Dispose();
            _tray.Visible = false;  // 先隐藏托盘（内部会访问 Icon.Handle）
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            // 统一走 ReleaseTarget 清理（不转待激活、不刷新 UI——窗体即将关闭）
            foreach (var e in _entries.ToList())
            {
                try { ReleaseTarget(e, "quit", updateUI: false); }
                catch { /* 单条清理失败不阻塞后续 */ }
            }
            _pendingPanels.Clear();
            _appIcon?.Dispose();  // 最后释放——NotifyIcon 已不再引用它
        }

        // ════════════════════════════════════════════════════════════
        // 入口
        // ════════════════════════════════════════════════════════════

        [STAThread]
        static void Main()
        {
            // PerMonitorV2 必须在 EnableVisualStyles 之前设置，否则静默失效（进程退化为系统 DPI 感知，
            // DeviceDpi 永远返回 96，主界面无法按缩放比缩放）。csproj 的 ApplicationHighDpiMode 也会注入相同声明作为兜底。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 开机自启（注册表 Run 项带 /startup 参数）时只驻留托盘、不弹主窗口；
            // 手动双击 exe 不带参数，则正常显示主窗口。
            bool startHidden = Environment.GetCommandLineArgs().Skip(1)
                .Any(a => a.Equals("/startup", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/background", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(startHidden));
        }
    }
}
