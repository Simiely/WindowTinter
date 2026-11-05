using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using WindowTinter.ViewModels;

namespace WindowTinter
{
    /// <summary>
    /// 托盘服务（Hardcodet TaskbarIcon + 自绘深色 WPF 菜单，对齐设计稿 E）：
    /// 菜单项保留原有功能语义：开合主窗口 / 拾取目标 / 全局默认参数 / 启停 / 退出。
    /// 菜单视觉全部来自 Themes/Dark.xaml（深色圆角 + 蓝色 hover），不再依赖系统菜单主题。
    /// </summary>
    internal class TrayService : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly MainViewModel _vm;
        private readonly Func<bool> _isWindowOpen;
        private readonly Action _toggleWindow;
        private readonly ContextMenu _menu;
        private readonly MenuItem _toggleItem;
        private readonly MenuItem _enableItem;

        public TrayService(MainViewModel vm, Func<bool> isWindowOpen, Action toggleWindow)
        {
            _vm = vm;
            _isWindowOpen = isWindowOpen;
            _toggleWindow = toggleWindow;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string ver = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "6.0.1";

            _toggleItem = Item("显示主窗口", "双击托盘", new RelayCommand(_toggleWindow));
            var pickItem = Item("拾取目标窗口", "", new RelayCommand(() => _vm.AddWindowCommand.Execute(null)));
            var settingsItem = Item("全局默认参数", "", new RelayCommand(() => _vm.OpenSettingsCommand.Execute(null)));
            _enableItem = Item("停用", "", new RelayCommand(() => _vm.IsEnabled = !_vm.IsEnabled));
            var exitItem = Item("退出暗幕", "", new RelayCommand(_vm.ExitApplication), danger: true);

            _menu = new ContextMenu
            {
                DataContext = _vm,
                MinWidth = 236,
                Tag = BuildHeader()
            };
            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(pickItem);
            _menu.Items.Add(settingsItem);
            _menu.Items.Add(new Separator());
            _menu.Items.Add(_enableItem);
            _menu.Items.Add(new Separator());
            _menu.Items.Add(exitItem);
            _menu.Opened += (_, _) =>
            {
                _toggleItem.Header = _isWindowOpen() ? "最小化到托盘" : "显示主窗口";
                _enableItem.Header = _vm.IsEnabled ? "停用" : "启用";
            };

            _icon = new TaskbarIcon
            {
                Icon = LoadAppIcon(),
                ToolTipText = $"暗幕 v{ver}",
                Visibility = Visibility.Visible,
                ContextMenu = _menu
            };
            _icon.TrayMouseDoubleClick += (_, _) => _toggleWindow();
        }

        /// <summary>菜单头：logo + 名称 + 实时汇总（设计稿 E 的 232 宽头部）。</summary>
        private UIElement BuildHeader()
        {
            var logo = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = Res("Accent"),
                Child = new TextBlock
                {
                    Text = "暗",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var name = new TextBlock
            {
                Text = "暗幕",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var summary = new TextBlock
            {
                FontSize = 9,
                Foreground = Res("Text3"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            summary.DataContext = _vm;
            summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.ActiveSummary)));

            var line0 = new Grid();
            line0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line0.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line0.Children.Add(new System.Windows.Controls.StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { logo, name }
            });
            Grid.SetColumn(summary, 1);
            line0.Children.Add(summary);

            var sub = new TextBlock
            {
                Text = "WindowTinter · 桌面压暗",
                FontSize = 9,
                Foreground = Res("Text3"),
                Margin = new Thickness(0, 5, 0, 0)
            };

            var sp = new System.Windows.Controls.StackPanel();
            sp.Children.Add(line0);
            sp.Children.Add(sub);
            return sp;
        }

        private MenuItem Item(string header, string gesture, ICommand command, bool danger = false)
        {
            var mi = new MenuItem
            {
                Header = header,
                InputGestureText = gesture,
                Command = command,
                Style = (Style)Application.Current.FindResource(danger ? "TrayMenuItemDanger" : "TrayMenuItem")
            };
            return mi;
        }

        private static System.Windows.Media.Brush Res(string key)
            => (System.Windows.Media.Brush)Application.Current.FindResource(key);

        /// <summary>加载嵌入程序集的 app.ico 作为托盘图标（发布无需外部 ico 文件）。</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/app.ico");
                using (var stream = Application.GetResourceStream(uri)?.Stream)
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            try { _menu.IsOpen = false; } catch { }
            try { _icon.Dispose(); } catch { }
        }
    }
}
