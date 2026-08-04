using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using WindowTinter.ViewModels;
using WindowTinter.Views;

namespace WindowTinter
{
    /// <summary>
    /// 托盘服务（Hardcodet.NotifyIcon.Wpf + 自绘 Popup 菜单）：
    /// - 图标用 Hardcodet 的 TaskbarIcon（稳定、轻量、WPF 风格 ToolTip）
    /// - 右键菜单**自绘**（根治 WPF ContextMenu 系统渲染 Foreground 不生效的根因）：
    ///   WPF ContextMenu 在 Win11 走系统渲染，MenuItem.Foreground 经常不穿透模板，
    ///   MenuItem 模板里可能用 Aero/AeroLite 系统主题 TextBlock 覆盖了 Foreground。
    ///   改为自绘 WPF Popup Window：完全可控、深色风格与主窗口一致、文字 100% 可见。
    /// - 监听 TaskbarIcon 的 TrayRightMouseUp，自己 Show() 弹出菜单。
    /// </summary>
    internal class TrayService : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly MainViewModel _vm;
        private readonly Func<bool> _isWindowOpen;
        private readonly Action _toggleWindow;

        public TrayService(MainViewModel vm, Func<bool> isWindowOpen, Action toggleWindow)
        {
            _vm = vm;
            _isWindowOpen = isWindowOpen;
            _toggleWindow = toggleWindow;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string ver = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "5.6.0";

            _icon = new TaskbarIcon
            {
                Icon = LoadAppIcon(),
                ToolTipText = $"暗幕 v{ver}",
                Visibility = System.Windows.Visibility.Visible
            };
            _icon.TrayMouseDoubleClick += (_, _) => _toggleWindow();
            _icon.TrayRightMouseUp += OnTrayRightMouseUp;
        }

        /// <summary>右键弹出自绘菜单。</summary>
        private void OnTrayRightMouseUp(object sender, RoutedEventArgs e)
        {
            var entries = new[]
            {
                new TrayMenuWindow.MenuEntry { Header = _vm.StatusText, Enabled = false },
                new TrayMenuWindow.MenuEntry { IsSeparator = true },
                new TrayMenuWindow.MenuEntry { Header = _isWindowOpen() ? "最小化到托盘" : "打开设置窗口",
                                               OnClick = _toggleWindow },
                new TrayMenuWindow.MenuEntry { Header = _vm.IsEnabled ? "⏸ 停用" : "▶ 启用",
                                               OnClick = () => _vm.IsEnabled = !_vm.IsEnabled },
                new TrayMenuWindow.MenuEntry { IsSeparator = true },
                new TrayMenuWindow.MenuEntry { Header = "退出", OnClick = () => _vm.ExitApplication() }
            };

            // 鼠标位置（屏幕物理坐标，WinForms 坐标系）
            var mousePos = System.Windows.Forms.Control.MousePosition;
            var menu = new TrayMenuWindow { Owner = GetOwnerWindow() };
            menu.Show(entries, mousePos);
        }

        /// <summary>菜单 Owner 设为主窗口（关联生命周期/层级）——取当前主窗口避免空引用。</summary>
        private Window GetOwnerWindow()
        {
            return Application.Current?.MainWindow is Window w && w.IsVisible ? w : null;
        }

        /// <summary>加载 exe 同目录 app.ico 作为托盘图标。</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(path)) return new Icon(path);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            try { _icon.Dispose(); } catch { }
        }
    }
}