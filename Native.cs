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

        // ---- 窗口样式（创建放大镜/分层窗口用）----
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_POPUP = 0x80000000;

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

        // ---- user32：UpdateLayeredWindow（逐像素 alpha 合成，修复黑屏）----
        public const uint ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;        // AC_SRC_OVER = 0
            public byte BlendFlags;     // 必须为 0
            public byte SourceConstantAlpha; // 0-255，整窗 alpha
            public byte AlphaFormat;    // AC_SRC_ALPHA = 1
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hWnd, IntPtr hdcDst,
            ref Point pptDst, ref Size psize,
            IntPtr hdcSrc, ref Point pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        // ---- gdi32：DC / 位图管理 ----
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // ---- user32：鼠标捕获（拾取器拖拽用）----
        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        // ---- user32：窗口 DC（拾取器高亮边框用）----
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        // ---- gdi32：PatBlt（反转绘制，拾取器高亮边框）----
        public const uint DSTINVERT = 0x00550009;

        [DllImport("gdi32.dll")]
        public static extern bool PatBlt(IntPtr hdc, int nXLeft, int nYLeft, int nWidth, int nHeight, uint dwRop);

        // ---- dwmapi：标题栏深色模式 (Windows 10 2004+) ----
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttr, int cbAttr);

        // ---- user32：拾取窗口 ----
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        public const uint GA_ROOT = 2;

        // ---- user32：Z 序遍历（遮挡检测用）----
        public const uint GW_HWNDNEXT = 2;   // 返回 Z 序中更靠后（下方）的窗口
        public const uint GW_HWNDPREV = 3;   // 返回 Z 序中更靠前（上方）的窗口

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        // ---- GDI：区域（HRGN）裁剪，用于让蒙版只盖目标可见区域 ----
        public const int RGN_AND = 1;
        public const int RGN_OR = 2;
        public const int RGN_XOR = 3;
        public const int RGN_DIFF = 4;   // 差集：从区域中挖掉另一区域
        public const int RGN_COPY = 5;

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

        [DllImport("gdi32.dll")]
        public static extern int OffsetRgn(IntPtr hrgn, int nXOffset, int nYOffset);

        [DllImport("gdi32.dll")]
        public static extern int GetRgnBox(IntPtr hrgn, out RECT lprc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        // ---- user32：WinEvent 钩子（事件驱动更新，替代高频轮询）----
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B; // 窗口移动/缩放
        public const uint EVENT_OBJECT_HIDE = 0x8004;
        public const uint EVENT_OBJECT_SHOW = 0x8006;
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint EVENT_OBJECT_ZORDERCHANGES = 0x8012;  // Z 序变化（遮挡可能变）
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;     // 前台窗口切换

        public delegate void WinEventProc(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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
