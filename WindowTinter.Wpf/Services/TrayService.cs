using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using WindowTinter.ViewModels;

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

            // 托盘菜单脱离应用主题：系统浅色菜单必须用系统文字色。
            // 否则 Dark.xaml 的全局 TextBlock 隐式样式（浅色 #E8EAF0）会应用到菜单文字，
            // 造成"浅字 + 系统浅色底"文字看不见（局部资源优先级高于 Application 资源）。
            var menuTextStyle = new Style(typeof(TextBlock));
            menuTextStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.SystemColors.MenuTextBrush));
            menu.Resources[typeof(TextBlock)] = menuTextStyle;

            menu.Items.Add(MenuStatus());
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(_isWindowOpen() ? "最小化到托盘" : "打开设置窗口", (_, _) => _toggleWindow()));
            menu.Items.Add(MenuItem(_vm.IsEnabled ? "⏸ 停用" : "▶ 启用", (_, _) => { _vm.IsEnabled = !_vm.IsEnabled; RefreshMenu(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("退出", (_, _) => _vm.ExitApplication()));
            _icon.ContextMenu = menu;
        }

        private MenuItem MenuStatus()
        {
            var mi = new MenuItem { Header = _vm.StatusText, IsEnabled = false };
            return mi;
        }

        private static MenuItem MenuItem(string header, System.Windows.RoutedEventHandler onClick)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += onClick;
            return mi;
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
