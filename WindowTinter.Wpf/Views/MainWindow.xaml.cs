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
            Loaded += (_, _) => BuildTray();
            // 标题栏深色（immersive dark mode），与背景 #1E2024 协调，消除默认白条
            WindowTheme.EnableDarkTitleBar(this);
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
            // 真实退出：释放托盘 + 全部还原（托盘释放异常不得中断还原流程）
            try { _tray?.Dispose(); } catch { }
            _vm.Shutdown();
        }
    }
}
