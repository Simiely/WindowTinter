using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

        /// <summary>构建菜单并在鼠标位置弹出。定位用纯 Win32 物理像素（GetCursorPos + SetWindowPos），
        /// 完全绕开 WPF Left/Top 的 DIP 换算——这是 SciChart 验证的跨 DPI 可靠方案。</summary>
        public void Show(IList<MenuEntry> entries)
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

            // 先移到屏幕外避免闪现，再 Show 建立句柄
            Left = -10000;
            Top = -10000;
            Show();
            Activate();

            // ── 纯物理像素定位 ──
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.GetCursorPos(out var pt);            // 鼠标物理像素
            Native.GetWindowRect(hwnd, out var rc);     // 窗口实际物理尺寸
            int w = rc.Width, h = rc.Height;

            int left = pt.X - w / 2;                    // 居中于鼠标 X
            int top = pt.Y - h - 8;                     // 鼠标上方 8px

            // 边界保护：鼠标所在显示器的工作区（物理像素）
            var mon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfoW(mon, ref mi))
            {
                int waL = mi.rcWork.Left, waT = mi.rcWork.Top;
                int waR = mi.rcWork.Right, waB = mi.rcWork.Bottom;
                if (left + w > waR) left = waR - w - 8;
                if (left < waL + 8) left = waL + 8;
                if (top < waT + 8) top = pt.Y + 12;      // 上方放不下就放下方
                if (top + h > waB) top = waB - h - 8;
            }

            // 两次 SetWindowPos：第一次触发 DPI Changed（若跨屏），第二次精确定位
            const uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
            Native.SetWindowPos(hwnd, IntPtr.Zero, left + 1, top + 1, 0, 0, flags);
            Native.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, flags);
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