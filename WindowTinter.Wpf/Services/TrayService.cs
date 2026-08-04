using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using WindowTinter.ViewModels;
// 别名：Color/Brush/FontFamily 默认指向 WPF，System.Drawing.* 完全限定
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace WindowTinter
{
    /// <summary>
    /// 托盘服务（Hardcodet.NotifyIcon.Wpf，对齐 WinForms MainForm.Tray.cs）：
    /// 状态文案 / 开合切换 / 启停 / 退出；双击托盘开合窗口。
    /// </summary>
    internal class TrayService : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly MainViewModel _vm;
        private readonly Func<bool> _isWindowOpen;
        private readonly Action _toggleWindow;

        // 托盘菜单色板（与主窗口 Themes/Dark.xaml 同源；显式赋值，绕过任何样式继承链）
        private static readonly Brush MENU_FG = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0)); // Text
        private static readonly Brush MENU_BG = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x24)); // Bg
        private static readonly Brush MENU_FG_DISABLED = new SolidColorBrush(Color.FromRgb(0x90, 0x97, 0xA0)); // Text3

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
            RefreshMenu();
        }

        /// <summary>刷新托盘菜单（对齐 WinForms RefreshTrayMenu）。</summary>
        public void RefreshMenu()
        {
            var menu = new ContextMenu();
            menu.Background = MENU_BG;
            menu.Foreground = MENU_FG;
            menu.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x46)); // Border

            menu.Items.Add(MenuStatus());
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MenuItem(_isWindowOpen() ? "最小化到托盘" : "打开设置窗口", (_, _) => _toggleWindow()));
            menu.Items.Add(MenuItem(_vm.IsEnabled ? "⏸ 停用" : "▶ 启用", (_, _) => { _vm.IsEnabled = !_vm.IsEnabled; RefreshMenu(); }));
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MenuItem("退出", (_, _) => _vm.ExitApplication()));
            _icon.ContextMenu = menu;
        }

        private MenuItem MenuStatus()
        {
            var mi = MakeMenuItem(_vm.StatusText, false);
            mi.Foreground = MENU_FG_DISABLED; // 状态文案用次要色
            return mi;
        }

        private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
        {
            var mi = MakeMenuItem(header, true);
            mi.Click += onClick;
            return mi;
        }

        /// <summary>托盘菜单项：显式 Background/Foreground/FontFamily，绕过所有继承链。</summary>
        private static MenuItem MakeMenuItem(string header, bool enabled)
        {
            var mi = new MenuItem
            {
                Header = header,
                Background = MENU_BG,
                Foreground = MENU_FG,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                IsEnabled = enabled
            };
            return mi;
        }

        private static Separator NewSeparator()
        {
            return new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x46))
            };
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
            return System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            try { _icon.Dispose(); } catch { }
        }
    }
}