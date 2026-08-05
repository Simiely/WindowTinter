using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using WindowTinter.ViewModels;

namespace WindowTinter.Views
{
    /// <summary>
    /// 主窗口：DataContext = MainViewModel。
    /// 关闭拦截：勾选"最小化到托盘"时隐藏驻留（效果继续）；真实退出（🚪/托盘退出）才释放全部。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private TrayService _tray;
        private bool _everShown; // 窗口是否曾真正显示过（静默启动从未显示时，退出不得覆盖已保存的窗口位置）

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            Closing += OnClosing;
            // 系统关机/注销：置 ReallyQuit 放行 OnClosing（否则托盘拦截会阻止系统关机——
            // WinForms 版踩过此坑，WPF 版用 SessionEnding 等价处理）
            Application.Current.SessionEnding += (_, _) => _vm.ReallyQuit = true;
            // 首次显示（句柄创建）时恢复窗口状态；静默启动（/startup）不显示窗口，则不做任何 UI 初始化
            SourceInitialized += (_, _) => { _everShown = true; RestoreWindowState(); };
            // 标题栏深色（immersive dark mode），与背景 #1E2024 协调，消除默认白条
            WindowTheme.EnableDarkTitleBar(this);
            // 托盘不依赖窗口显示：构造函数即建立。开机自启静默时 App 不调用 Show()，
            // 窗口句柄完全不创建（连一帧都不渲染），仅托盘图标驻留——官方推荐的零闪烁做法。
            BuildTray();
        }

        /// <summary>从 Settings 恢复窗口位置/尺寸/最大化（SourceInitialized 时调用，确保 DPI/屏幕信息就绪）。</summary>
        private void RestoreWindowState()
        {
            var s = _vm.GetSettings();
            if (s.WindowWidth > 50) Width = s.WindowWidth;
            if (s.WindowHeight > 50) Height = s.WindowHeight;
            if (s.WindowLeft >= 0 && s.WindowTop >= 0)
            {
                // 校验位置在可见屏幕内（多屏拔掉后防止窗口飞出）
                var vLeft = SystemParameters.VirtualScreenLeft;
                var vTop = SystemParameters.VirtualScreenTop;
                var vRight = vLeft + SystemParameters.VirtualScreenWidth;
                var vBottom = vTop + SystemParameters.VirtualScreenHeight;
                if (s.WindowLeft + Width > vLeft + 50 && s.WindowLeft < vRight - 50 &&
                    s.WindowTop + Height > vTop + 50 && s.WindowTop < vBottom - 50)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = s.WindowLeft;
                    Top = s.WindowTop;
                }
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
        }

        /// <summary>保存窗口最终位置/尺寸/最大化到 Settings（真实退出时调用）。
        /// 窗口从未显示过（静默启动直接退出）时跳过，避免用默认坐标覆盖用户已保存的位置。</summary>
        private void SaveWindowState()
        {
            if (!_everShown) return;
            var s = _vm.GetSettings();
            s.WindowMaximized = WindowState == WindowState.Maximized;
            // 最大化时保存还原尺寸（Left/Top/Width/Height=RestoreBounds 才有值）
            if (WindowState == WindowState.Normal)
            {
                s.WindowLeft = Left;
                s.WindowTop = Top;
                s.WindowWidth = Width;
                s.WindowHeight = Height;
            }
            else if (RestoreBounds != Rect.Empty)
            {
                s.WindowLeft = RestoreBounds.Left;
                s.WindowTop = RestoreBounds.Top;
                s.WindowWidth = RestoreBounds.Width;
                s.WindowHeight = RestoreBounds.Height;
            }
        }

        /// <summary>卡片整卡点击选中：非全局时点卡片任意处 = 选中（✎/× 按钮点击除外）。</summary>
        private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is ButtonBase) return; // 按钮自身操作，不触发选中
            if (sender is FrameworkElement fe && fe.DataContext is TargetViewModel vm)
                vm.SelectCommand.Execute(null);
        }

        private void BuildTray() => _tray = new TrayService(_vm, IsWindowOpen, ToggleWindow);

        /// <summary>窗口是否真正"打开"（可见且非最小化）——托盘菜单文案与开合判断。</summary>
        private bool IsWindowOpen() => IsVisible && WindowState != WindowState.Minimized;

        /// <summary>开合切换（对齐 WinForms Tray.ToggleWindow）。</summary>
        private void ToggleWindow()
        {
            if (IsWindowOpen())
            {
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                ShowInTaskbar = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_vm.ReallyQuit && _vm.MinimizeToTray)
            {
                // 最小化到托盘：只存盘，效果继续运行
                _vm.SaveSettings();
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            // 真实退出：保存窗口状态 → 释放托盘 → 全部还原 → 显式结束消息循环
            // （ShutdownMode=OnExplicitShutdown 下关闭窗口不会退出应用）
            try { SaveWindowState(); _vm.SaveSettings(); } catch { }
            try { _tray?.Dispose(); } catch { }
            _vm.Shutdown();
            try { Application.Current.Shutdown(); } catch { }
        }
    }
}
