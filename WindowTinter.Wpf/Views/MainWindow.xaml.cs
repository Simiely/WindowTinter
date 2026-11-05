using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
            SourceInitialized += (_, _) => { RestoreWindowState(); InstallMaximizeClamp(); };
            Loaded += (_, _) => { BuildTray(); AdaptEmptyHeight(); };
            _vm.Targets.CollectionChanged += (_, _) => AdaptEmptyHeight();
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
                // 空状态自动收缩的高度不写入配置，避免下次启动把 560 当成用户尺寸
                s.WindowHeight = _autoShrunk ? (_heightBeforeEmpty > 0 ? _heightBeforeEmpty : 720) : Height;
            }
            else if (RestoreBounds != Rect.Empty)
            {
                s.WindowLeft = RestoreBounds.Left;
                s.WindowTop = RestoreBounds.Top;
                s.WindowWidth = RestoreBounds.Width;
                s.WindowHeight = RestoreBounds.Height;
            }
        }

        /// <summary>卡片整卡点击选中：非全局时点卡片任意处 = 选中（按钮点击除外）。</summary>
        private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 命中测试的最内层元素通常是按钮里的 Path/Ellipse，不是 ButtonBase 本身：
            // 沿可视树向上找，只要落在按钮内就不触发卡片的选中/展开（例如单卡开关）。
            if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;
            if (sender is FrameworkElement fe && fe.DataContext is TargetViewModel vm)
                vm.SelectCommand.Execute(null);
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as T;
        }

        /// <summary>卡片键盘可达：Tab 聚焦后 Enter / Space 展开。</summary>
        private void Card_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            if (sender is FrameworkElement fe && fe.DataContext is TargetViewModel vm)
            {
                vm.SelectCommand.Execute(null);
                e.Handled = true;
            }
        }

        // ── 无边框窗口最大化：按显示器工作区钳制（否则 Win10 会连任务栏区域一起盖掉 + 顶部越界 7px）──
        private void InstallMaximizeClamp()
        {
            if (PresentationSource.FromVisual(this) is HwndSource src)
                src.AddHook(OnWindowMessage);
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_GETMINMAXINFO) return IntPtr.Zero;

            var mmi = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfoW(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi))
                return IntPtr.Zero;

            // 最大化矩形 = 工作区向外扩一圈不可见缩放边框（客户区正好等于工作区）
            int fx = Native.GetSystemMetrics(Native.SM_CXSIZEFRAME) + Native.GetSystemMetrics(Native.SM_CXPADDEDBORDER);
            int fy = Native.GetSystemMetrics(Native.SM_CYSIZEFRAME) + Native.GetSystemMetrics(Native.SM_CXPADDEDBORDER);

            mmi.ptMaxPosition.X = mi.rcWork.Left - fx;
            mmi.ptMaxPosition.Y = mi.rcWork.Top - fy;
            mmi.ptMaxSize.X = mi.rcWork.Width + 2 * fx;
            mmi.ptMaxSize.Y = mi.rcWork.Height + 2 * fy;

            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        // ── 标题栏窗控（自绘 chrome）──
        private void OnMinClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaxClick(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ── 空状态高度自适应（空列表最小 560，恢复时回到用户原高度）──
        private double _heightBeforeEmpty;
        private bool _autoShrunk;

        private void AdaptEmptyHeight()
        {
            if (WindowState != WindowState.Normal) return;
            if (_vm.IsEmpty)
            {
                if (Height > 560)
                {
                    _heightBeforeEmpty = Height;
                    Height = 560;
                    _autoShrunk = true;
                }
            }
            else if (_autoShrunk && Height <= 560.5)
            {
                Height = _heightBeforeEmpty > 0 ? _heightBeforeEmpty : 720;
                _autoShrunk = false;
            }
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
