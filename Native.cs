using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowTinter
{
    /// <summary>
    /// 全部 Win32 / Magnification P/Invoke 声明集中在此。
    /// </summary>
    internal static class Native
    {
        // ---- 窗口扩展样式 ----
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;

        // ---- SetWindowPos ----
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        // ---- 分层窗口属性 ----
        public const int LWA_ALPHA = 0x2;
        public const int LWA_COLORKEY = 0x1;

        // ---- 消息 ----
        public const int WM_HOTKEY = 0x0312;

        // ---- 热键修饰符 ----
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_WIN = 0x8;

        // ---- 窗口样式（创建放大镜用）----
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_VISIBLE = 0x10000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        // Magnifier 3x3 变换矩阵
        [StructLayout(LayoutKind.Sequential)]
        public struct MAGTRANSFORM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] v;
        }

        // Magnifier 5x5 颜色变换矩阵
        [StructLayout(LayoutKind.Sequential)]
        public struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        // ---- user32：窗口枚举 / 文本 / 进程 ----
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        // ---- user32：几何 / 样式 ----
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        // ---- user32：热键 ----
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ---- user32：拾取窗口 ----
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);

        // ---- user32：创建窗口（放大镜宿主/控件）----
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        // ---- Magnification.dll ----
        [DllImport("Magnification.dll")]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll")]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll")]
        public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

        [DllImport("Magnification.dll")]
        public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM pTransform);

        [DllImport("Magnification.dll")]
        public static extern bool MagSetColorEffect(IntPtr hwnd, ref MAGCOLOREFFECT pEffect);
    }
}
