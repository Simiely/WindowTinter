using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using WindowTinter.ViewModels;

namespace WindowTinter
{
    /// <summary>
    /// 托盘服务（Hardcodet.NotifyIcon.Wpf 图标 + Win32 原生菜单）：
    /// - 图标用 Hardcodet 的 TaskbarIcon（稳定、轻量、WPF 风格 ToolTip）
    /// - 右键菜单用 **Win32 TrackPopupMenu 原生系统菜单**（微软官方推荐的托盘菜单模式）：
    ///   CreatePopupMenu + AppendMenuW + TrackPopupMenu(TPM_RETURNCMD) + DestroyMenu。
    ///   依据：H.NotifyIcon（Hardcodet 继任项目）官方 README 确认"托盘菜单会被转换为 Win32
    ///   PopupMenu"——WPF ContextMenu 的 Foreground/Background 对系统菜单不生效，文字色由
    ///   系统主题决定。所以直接用 Win32 原生菜单 = 与 WinForms 主版（用户验证过功能完美）
    ///   完全相同的渲染路径：文字色/主题自动跟随系统，永不失明、永不样式污染。
    /// - 微软官方模式：SetForegroundWindow → TrackPopupMenu → PostMessage(WM_NULL)，
    ///   保证菜单定位正确且点击外部能正常消失。
    /// - **关键**：SetForegroundWindow/TrackPopupMenu 的 owner 用**专用隐藏辅助窗口**
    ///   （_menuOwnerHwnd），**绝不使用主窗口句柄**——否则右键托盘会把主窗口带到 Z 序最前。
    /// </summary>
    internal class TrayService : IDisposable
    {
        private const uint WM_NULL = 0x0000;
        private const uint WS_POPUP = 0x80000000;

        // 菜单项 ID
        private const uint ID_STATUS = 1;
        private const uint ID_TOGGLE = 2;
        private const uint ID_ENABLE = 3;
        private const uint ID_EXIT = 4;

        private readonly TaskbarIcon _icon;
        private readonly MainViewModel _vm;
        private readonly Func<bool> _isWindowOpen;
        private readonly Action _toggleWindow;
        private readonly IntPtr _menuOwnerHwnd;

        public TrayService(MainViewModel vm, Func<bool> isWindowOpen, Action toggleWindow)
        {
            _vm = vm;
            _isWindowOpen = isWindowOpen;
            _toggleWindow = toggleWindow;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string ver = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "5.6.0";

            // 专用隐藏辅助窗口：作为 TrackPopupMenu 的 owner（接收菜单消息）。
            // 不显示、不进任务栏，唯一用途是让 SetForegroundWindow 作用于它而非主窗口。
            _menuOwnerHwnd = Native.CreateWindowExW(0, "Static", "WindowTinter.TrayMenuOwner",
                WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                Native.GetModuleHandleW(null), IntPtr.Zero);

            _icon = new TaskbarIcon
            {
                Icon = LoadAppIcon(),
                ToolTipText = $"暗幕 v{ver}",
                Visibility = System.Windows.Visibility.Visible
            };
            _icon.TrayMouseDoubleClick += (_, _) => _toggleWindow();
            _icon.TrayRightMouseUp += OnTrayRightMouseUp;
        }

        /// <summary>右键弹出 Win32 原生菜单（TrackPopupMenu，微软官方托盘菜单模式）。</summary>
        private void OnTrayRightMouseUp(object sender, RoutedEventArgs e)
        {
            IntPtr hMenu = Native.CreatePopupMenu();
            try
            {
                // 状态项（置灰不可点）
                Native.AppendMenuW(hMenu, Native.MF_STRING | Native.MF_GRAYED, ID_STATUS, _vm.StatusText);
                Native.AppendMenuW(hMenu, Native.MF_SEPARATOR, 0, null);
                // 开合切换
                Native.AppendMenuW(hMenu, Native.MF_STRING, ID_TOGGLE, _isWindowOpen() ? "最小化到托盘" : "打开设置窗口");
                // 启停切换
                Native.AppendMenuW(hMenu, Native.MF_STRING, ID_ENABLE, _vm.IsEnabled ? "停用" : "启用");
                Native.AppendMenuW(hMenu, Native.MF_SEPARATOR, 0, null);
                // 退出
                Native.AppendMenuW(hMenu, Native.MF_STRING, ID_EXIT, "退出");

                // 微软官方模式（TrackPopupMenu 文档）：
                // 1) SetForegroundWindow(辅助窗口) 保证菜单能正常消失与定位，且不打扰主窗口
                // 2) TrackPopupMenu(TPM_RIGHTBUTTON|TPM_RETURNCMD|TPM_NONOTIFY) 阻塞返回选中项 ID
                // 3) PostMessage(WM_NULL) 纠正"第二次显示立即消失"问题
                Native.GetCursorPos(out var pt);
                Native.SetForegroundWindow(_menuOwnerHwnd);
                uint cmd = Native.TrackPopupMenu(hMenu,
                    Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY,
                    pt.X, pt.Y, 0, _menuOwnerHwnd, IntPtr.Zero);
                Native.PostMessage(_menuOwnerHwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                switch (cmd)
                {
                    case ID_TOGGLE: _toggleWindow(); break;
                    case ID_ENABLE: _vm.IsEnabled = !_vm.IsEnabled; break;
                    case ID_EXIT: _vm.ExitApplication(); break;
                }
            }
            finally
            {
                Native.DestroyMenu(hMenu);
            }
        }

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
            try { _icon.Dispose(); } catch { }
            if (_menuOwnerHwnd != IntPtr.Zero)
            {
                try { Native.DestroyWindow(_menuOwnerHwnd); } catch { }
            }
        }
    }
}