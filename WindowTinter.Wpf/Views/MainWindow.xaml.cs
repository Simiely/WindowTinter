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

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            Closing += OnClosing;
            // 加载 → 恢复窗口状态 → 构建托盘
            SourceInitialized += (_, _) => RestoreWindowState();
            Loaded += (_, _) => BuildTray();
            // 标题栏深色（immersive dark mode），与背景 #1E2024 协调，消除默认白条
            WindowTheme.EnableDarkTitleBar(this);
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

        /// <summary>保存窗口最终位置/尺寸/最大化到 Settings（真实退出时调用）。</summary>
        private void SaveWindowState()
        {
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
            // 真实退出：保存窗口状态 → 释放托盘 → 全部还原（托盘释放异常不得中断还原流程）
            try { SaveWindowState(); _vm.SaveSettings(); } catch { }
            try { _tray?.Dispose(); } catch { }
            _vm.Shutdown();
        }
    }
}
