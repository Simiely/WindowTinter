using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
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

        // ── 圆角调参（Ctrl+Alt+方向键，±1px；默认值与正式版一致）──
        private int _cornerOuter = 14;   // 窗口外轮廓（HRGN / WindowChrome）
        private int _cornerFrame = 14;   // 根 Border 描边弧（与外轮廓分开，才能对齐角上的差）
        private int _cornerClip = 13;    // 根内容裁切
        private int _cornerInset;        // 裁切矩形相对窗口边缘的内缩
        private bool _cornerDebugVisible;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            LoadCornerDebugSettings();
            Closing += OnClosing;
            // 加载 → 恢复窗口状态 → 构建托盘
            SourceInitialized += (_, _) => { RestoreWindowState(); InstallMaximizeClamp(); };
            Loaded += (_, _) => { BuildTray(); AdaptEmptyHeight(); };
            _vm.Targets.CollectionChanged += (_, _) => AdaptEmptyHeight();
            // 标题栏深色（immersive dark mode），与背景 #1E2024 协调，消除默认白条
            WindowTheme.EnableDarkTitleBar(this);
            // 主窗圆角唯一来源：根 Border 14px；内容按内圆角裁切，最大化时窗口为直角则同步取消
            RootContent.SizeChanged += (_, _) => UpdateFrameClip();
            StateChanged += (_, _) => UpdateFrameClip();
            DpiChanged += (_, _) => UpdateFrameClip();
            Loaded += (_, _) => { ApplyCornerDebug(); UpdateCornerDebugReadout(); };
        }

        /// <summary>读入上次调过的圆角参数（无该段 → 保持正式默认 14/13/0，不创建对象）。</summary>
        private void LoadCornerDebugSettings()
        {
            var dbg = _vm.GetSettings().DebugCorner;
            if (dbg == null) return;
            _cornerOuter = Math.Clamp(dbg.OuterRadius, 0, 32);
            _cornerFrame = Math.Clamp(dbg.FrameRadius, 0, 32);
            _cornerClip = Math.Clamp(dbg.ClipRadius, 0, 32);
            _cornerInset = Math.Clamp(dbg.BorderInset, 0, 6);
            _cornerDebugVisible = true;
        }

        /// <summary>把调试值同步到 WindowChrome 与根 Border/裁切，并重画窗口区域。</summary>
        private void ApplyCornerDebug()
        {
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null) chrome.CornerRadius = new CornerRadius(_cornerOuter);
            UpdateFrameClip();
        }

        /// <summary>
        /// 圆角收口：根 Border 画外轮廓与 1px 描边，根内容 Grid 按内圆角裁切，
        /// 避免标题栏/底栏的矩形底色越过 1px 弧线。最大化时切直角并取消裁切。
        /// </summary>
        private void UpdateFrameClip()
        {
            if (RootFrame == null || RootContent == null) return;
            bool maximized = WindowState == WindowState.Maximized;
            RootFrame.CornerRadius = new CornerRadius(maximized ? 0 : _cornerFrame);

            double w = RootContent.ActualWidth, h = RootContent.ActualHeight;
            double inset = maximized ? 0 : _cornerInset;
            if (maximized || w <= 0 || h <= 0 || w - inset * 2 <= 0 || h - inset * 2 <= 0)
            {
                RootContent.Clip = null;
            }
            else
            {
                RootContent.Clip = new RectangleGeometry(
                    new Rect(inset, inset, w - inset * 2, h - inset * 2), _cornerClip, _cornerClip);
            }

            ApplyOuterRegion(maximized);
        }

        /// <summary>
        /// 直接 SetWindowRgn 重画外轮廓，让调参即时生效。
        /// WindowChrome 的 CornerRadius 运行时改动只触发重绘：_SetRoundingRegion 在窗口状态未变时
        /// 直接 return，不会重建 HRGN，所以这里按 WindowChromeWorker 的同一算法自己重建。
        /// 最大化仍交给 WindowChrome 的直角区域，避免在满屏窗口里挖圆角。
        /// </summary>
        private void ApplyOuterRegion(bool maximized)
        {
            if (maximized || WindowState == WindowState.Minimized) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            int pw = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            int ph = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            int r = (int)Math.Ceiling(_cornerOuter * dpi.DpiScaleX);
            // 与 WindowChromeWorker 一致：右下各补 1px，半径按设备像素取整
            IntPtr rgn = Native.CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r);
            if (rgn == IntPtr.Zero) return;
            if (Native.SetWindowRgn(hwnd, rgn, true) == 0)
                Native.DeleteObject(rgn);   // 失败时区域仍归我们；成功由系统接管
        }

        /// <summary>Ctrl+Alt+↑/↓ 外轮廓、←/→ 裁切；再加 Shift+↑/↓ 调内缩。命中返回 true。</summary>
        private bool HandleCornerDebugKeys(KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            const ModifierKeys need = ModifierKeys.Control | ModifierKeys.Alt;
            if ((mods & need) != need) return false;

            int step;
            switch (e.Key)
            {
                case Key.Up: step = 1; break;
                case Key.Down: step = -1; break;
                case Key.Left: step = -1; break;
                case Key.Right: step = 1; break;
                default: return false;
            }

            if ((mods & ModifierKeys.Shift) != 0)
            {
                // Shift：↑↓ 调内缩，←→ 调根 Border 描边弧
                if (e.Key == Key.Up || e.Key == Key.Down)
                    _cornerInset = Math.Clamp(_cornerInset + step, 0, 6);
                else
                    _cornerFrame = Math.Clamp(_cornerFrame + step, 0, 32);
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                _cornerOuter = Math.Clamp(_cornerOuter + step, 0, 32);
            }
            else
            {
                _cornerClip = Math.Clamp(_cornerClip + step, 0, 32);
            }

            _cornerDebugVisible = true;
            PersistCornerDebug();
            ApplyCornerDebug();
            UpdateCornerDebugReadout();
            e.Handled = true;
            return true;
        }

        /// <summary>调过的值写入独立 debug 段（默认 null 不写盘）；下次启动沿用。</summary>
        private void PersistCornerDebug()
        {
            var s = _vm.GetSettings();
            s.DebugCorner = new DebugCornerSettings
            {
                OuterRadius = _cornerOuter,
                FrameRadius = _cornerFrame,
                ClipRadius = _cornerClip,
                BorderInset = _cornerInset
            };
            s.Save();
        }

        private void UpdateCornerDebugReadout()
        {
            if (CornerDebugText == null) return;
            // “外 14”是 DIP 设定值，“/18px”是实际下发给 HRGN 的设备像素半径（取整后）
            int device = (int)Math.Ceiling(_cornerOuter * VisualTreeHelper.GetDpi(this).DpiScaleX);
            CornerDebugText.Text = $"外 {_cornerOuter}/{device}px · 框 {_cornerFrame} · 裁 {_cornerClip} · 缩 {_cornerInset}";
            CornerDebugText.Visibility = _cornerDebugVisible ? Visibility.Visible : Visibility.Collapsed;
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

        /// <summary>卡片键盘可达：Tab 聚焦后 Enter / Space 展开（已展开则保持展开）。</summary>
        private void Card_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            if (sender is FrameworkElement fe && fe.DataContext is TargetViewModel vm)
            {
                vm.ExpandCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>chevron 单独点击：只折叠/展开这张卡，不把目标带到前台、不滚动列表。</summary>
        private void Card_Caret_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TargetViewModel vm)
                vm.CaretCommand.Execute(null);
        }

        /// <summary>Esc 收起当前展开卡片；滑块拖动中先释放捕获结束拖动（本次不收起）。</summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (HandleCornerDebugKeys(e)) return;
            if (e.Key != Key.Escape) return;
            if (Mouse.Captured is Thumb)
            {
                Mouse.Capture(null);
                e.Handled = true;
                return;
            }
            if (_vm.SelectedTarget == null) return;
            _vm.CollapseTarget();
            e.Handled = true;
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
