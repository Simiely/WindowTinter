using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowTinter
{
    /// <summary>
    /// 鍏ㄩ儴 Win32 P/Invoke 澹版槑闆嗕腑鍦ㄦ銆?
    /// </summary>
    internal static class Native
    {
        // ---- 绐楀彛鎵╁睍鏍峰紡 ----
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;

        // ---- SetWindowPos ----
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;   // 淇濇寔鍘?Z 搴忥紙HidePlate 绛夐殣钘?瀹氫綅鍦烘櫙閬垮厤骞叉壈 Z 搴忥級
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        // ---- 鍒嗗眰绐楀彛灞炴€?----
        public const int LWA_ALPHA = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        // ---- user32锛氱獥鍙ｆ灇涓?/ 鏂囨湰 / 杩涚▼ ----
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

        // ---- user32锛氬嚑浣?/ 鏍峰紡 ----
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        // ---- user32锛歎pdateLayeredWindow锛堥€愬儚绱?alpha 鍚堟垚锛?---
        public const uint ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(
            IntPtr hWnd, IntPtr hdcDst,
            ref Point pptDst, ref Size psize,
            IntPtr hdcSrc, ref Point pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        // ---- gdi32锛欴C / 浣嶅浘绠＄悊 ----
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

        // ---- user32锛氱獥鍙?DC锛堟嬀鍙栧櫒楂樹寒杈规鐢級----
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        // ---- gdi32锛歅atBlt锛堝弽杞粯鍒讹紝鎷惧彇鍣ㄩ珮浜竟妗嗭級----
        public const uint DSTINVERT = 0x00550009;

        [DllImport("gdi32.dll")]
        public static extern bool PatBlt(IntPtr hdc, int nXLeft, int nYLeft, int nWidth, int nHeight, uint dwRop);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        // ---- gdi32锛氬尯鍩熷垱寤猴紙鍦嗚鐭╁舰瑁佸壀鐢級----
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        // ---- user32锛氳缃獥鍙ｅ尯鍩?----
        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        // ---- dwmapi锛氭爣棰樻爮娣辫壊妯″紡 (Windows 10 2004+) ----
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttr, int cbAttr);

        // ---- dwmapi锛氭墿灞曟鏋惰竟鐣岋紙鎺掗櫎 DWM 闃村奖鍚庣殑鐪熷疄鍙鐭╁舰锛?---
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        /// <summary>鍙栫湡瀹炲彲瑙佺煩褰紙鎺掗櫎 DWM 闃村奖锛夛紝澶辫触鏃跺洖閫€ GetWindowRect銆?/summary>
        public static RECT GetVisibleWindowRect(IntPtr hwnd)
        {
            var r = default(RECT);
            // DwmGetWindowAttribute 浠呭湪 Windows Vista+ 鍙敤锛屾垜浠殑 target 鏄?net6-windows 娌￠棶棰?
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>());
            if (hr != 0 && !GetWindowRect(hwnd, out r))
                r = default; // 鍙岄噸鍥為€€鍧囧け璐ワ紝杩斿洖鍏ㄩ浂锛堣皟鐢ㄦ柟鏈?w<=0 闃叉姢锛?
            return r;
        }

        // ---- user32锛氭嬀鍙栫獥鍙?----
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        public const uint GA_ROOT = 2;

        /// <summary>鍙栨寚瀹氬睆骞曠偣涓?Z 搴忔渶闈犲墠锛堜笖閫氳繃鍛戒腑娴嬭瘯锛夌殑绐楀彛锛屽惈瀛愮獥鍙ｃ€傛瘮 EnumWindows+鐭╁舰鍖呭惈绮剧‘寰楀銆?/summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point pt);

        /// <summary>鑾峰彇绐楀彛绫诲悕锛堢敤浜庢嬀鍙栬繃婊や笌閲嶇粦瀹氬尮閰嶏級銆?/summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>鍙栫獥鍙ｆ墍鍦ㄧ洃瑙嗗櫒鐨?DPI锛圥erMonitorV2 杩涚▼涓嬪仛鐗╃悊鈫旈€昏緫鍧愭爣鎹㈢畻鐢級銆傚け璐ヨ繑鍥?0銆?/summary>
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        // ---- user32锛歐inEvent 閽╁瓙锛堜簨浠堕┍鍔ㄦ洿鏂帮紝鏇夸唬楂橀杞锛?---
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint EVENT_OBJECT_HIDE = 0x8003;        // 淇锛氭鍓嶈鍐欎负 0x8004锛圧EORDER锛?
        public const uint EVENT_OBJECT_SHOW = 0x8002;        // 淇锛氭鍓嶈鍐欎负 0x8006锛圫ELECTION锛?
        public const uint EVENT_OBJECT_REORDER = 0x8004;     // Z 搴忓彉鍖栵紙鍘熷瀷 B锛氭敹鍒板嵆閲嶆彃榛戝簳锛?
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        public delegate void WinEventProc(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // ---- uxtheme锛氭繁鑹叉粴鍔ㄦ潯 ----
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        // ---- user32锛氱獥鍙ｅ揩鐓э紙PrintWindow锛?----
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        // ---- user32锛氶噸缁?----
        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ERASE = 0x0004;
        public const uint RDW_FRAME = 0x0400;
        public const uint RDW_ALLCHILDREN = 0x0080;

        // ---- 鎻愭潈妫€娴嬶細鐩爣绐楀彛浠ョ鐞嗗憳杩愯銆佹湰绋嬪簭闈炴彁鏉冩椂鏃犳硶淇敼鍏堕€忔槑搴?----
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint TOKEN_QUERY = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_ELEVATION
        {
            public uint TokenIsElevated;
        }

        [DllImport("advapi32.dll")]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll")]
        public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
            out TOKEN_ELEVATION tokenInformation, uint tokenInformationLength, out uint returnLength);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        // ---- user32/kernel32：纯 Win32 窗口（BlackPlate 黑底，脱离 WPF 布局系统，物理像素直控）----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string lpModuleName);

        // ---- user32：托盘菜单定位（物理像素直控，绕开 WPF Left/Top DIP 换算）----
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        // ---- user32：托盘菜单（Win32 原生菜单 CreatePopupMenu + TrackPopupMenu，
        //      微软官方推荐的托盘菜单模式；文字色/主题由系统自动处理，永不失明）----
        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        public static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // 菜单项标志
        public const uint MF_STRING = 0x0;
        public const uint MF_SEPARATOR = 0x800;
        public const uint MF_GRAYED = 0x1;
        public const uint MF_DISABLED = 0x2;

        // TrackPopupMenu 标志
        public const uint TPM_LEFTBUTTON = 0x0;
        public const uint TPM_RIGHTBUTTON = 0x2;
        public const uint TPM_RETURNCMD = 0x100;
        public const uint TPM_NONOTIFY = 0x80;
    }
}

