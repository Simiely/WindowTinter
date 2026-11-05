using System;
using System.Windows;
using System.Windows.Interop;

namespace WindowTinter
{
    /// <summary>
    /// 窗口标题栏深色化（immersive dark mode，消除默认白条，与主界面深色背景协调）。
    /// 适用所有带标题栏的窗口：MainWindow / AboutDialog / RenameDialog。
    /// </summary>
    internal static class WindowTheme
    {
        public static void EnableDarkTitleBar(Window win)
        {
            win.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;
                int attr = 1;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref attr, sizeof(int));
            };
        }
    }
}
