using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowTinter.Views
{
    /// <summary>
    /// 托盘菜单（自绘 WPF Popup）：绕开 WPF ContextMenu 系统渲染 Foreground 不生效的根因。
    /// 完全可控：背景 #1E2024、文字 #E8EAF0、悬停 #FF9292 主调、字体 Microsoft YaHei UI。
    /// </summary>
    public partial class TrayMenuWindow : Window
    {
        public class MenuEntry
        {
            public string Header { get; set; }
            public bool Enabled { get; set; } = true;
            public bool IsSeparator { get; set; }
            public Action OnClick { get; set; }
        }

        // 菜单项三态色（与主窗口 Themes/Dark.xaml 同源）
        private static readonly Brush FG = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0));
        private static readonly Brush FG_DISABLED = new SolidColorBrush(Color.FromRgb(0x90, 0x97, 0xA0));
        private static readonly Brush HOVER = new SolidColorBrush(Color.FromRgb(0xFF, 0x92, 0x92));
        private static readonly Brush HOVER_BG = new SolidColorBrush(Color.FromRgb(0x2B, 0x3B, 0x52));
        private static readonly Brush SEP = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x46));

        public TrayMenuWindow()
        {
            InitializeComponent();
            // 窗口外点击关闭（Deactivated 也会触发）
            Deactivated += (_, _) => Close();
            // 拦截键盘 Esc 关闭
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        /// <summary>构建菜单：在鼠标位置弹出。mousePosDip = 屏幕 DIP 坐标（Mouse.GetPosition(null) 返回）。</summary>
        public void Show(IList<MenuEntry> entries, Point mousePosDip)
        {
            Panel.Children.Clear();
            foreach (var e in entries)
            {
                if (e.IsSeparator)
                {
                    Panel.Children.Add(new Border
                    {
                        Background = SEP,
                        Height = 1,
                        Margin = new Thickness(8, 4, 8, 4)
                    });
                    continue;
                }
                Panel.Children.Add(BuildItem(e));
            }

            // 关键（搜索确认的 WPF DPI 行为）：
            // Window.Left/Top 单位是 DIP；必须先 Show() 建立 HwndSource（PresentationSource 才会非 null），
            // 且 SizeToContent 布局完成后 ActualWidth/ActualHeight 才有效。
            // 先移到屏幕外避免闪现，Show 后再计算真实位置。
            Left = -10000;
            Top = -10000;
            Show();
            Activate();

            // Show 后布局完成：ActualWidth/ActualHeight 为最终尺寸（DIP）
            double w = ActualWidth;
            double h = ActualHeight;
            double left = mousePosDip.X - w / 2;   // 居中于鼠标 X
            double top = mousePosDip.Y - h - 8;     // 鼠标上方 8px（避免遮挡托盘图标）

            // 边界保护（虚拟屏幕 = 多屏并集，单位 DIP）
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vRight = vLeft + SystemParameters.VirtualScreenWidth;
            double vBottom = vTop + SystemParameters.VirtualScreenHeight;
            if (left + w > vRight) left = vRight - w - 8;
            if (left < vLeft + 8) left = vLeft + 8;
            if (top < vTop + 8) top = mousePosDip.Y + 12; // 上方放不下就放下方
            if (top + h > vBottom) top = vBottom - h - 8;

            Left = left;
            Top = top;
        }

        /// <summary>单菜单项：自定义 Border 渲染（避开 MenuItem 模板的样式继承坑）。</summary>
        private static UIElement BuildItem(MenuEntry e)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(14, 8, 14, 8),
                Cursor = e.Enabled ? Cursors.Hand : Cursors.Arrow
            };
            var tb = new TextBlock
            {
                Text = e.Header,
                FontSize = 12,
                Foreground = e.Enabled ? FG : FG_DISABLED
            };
            border.Child = tb;

            if (!e.Enabled) return border;

            // 悬停效果
            border.MouseEnter += (_, _) =>
            {
                border.Background = HOVER_BG;
                tb.Foreground = HOVER;
            };
            border.MouseLeave += (_, _) =>
            {
                border.Background = Brushes.Transparent;
                tb.Foreground = FG;
            };
            border.MouseLeftButtonUp += (_, _) =>
            {
                e.OnClick?.Invoke();
                var w = Window.GetWindow(border);
                w?.Close();
            };
            return border;
        }
    }
}