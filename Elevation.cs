using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowTinter
{
    /// <summary>
    /// 提权检测工具：判断目标进程 / 当前进程是否以管理员权限运行。
    /// 垫底方案需修改目标窗口样式（WS_EX_LAYERED），管理员目标在非提权进程下会静默失败——
    /// 绑定流程据此给出提示。
    /// </summary>
    internal static class Elevation
    {
        /// <summary>目标窗口所在进程是否已提权。无法判定时返回 null。</summary>
        public static bool? IsTargetElevated(IntPtr hwnd)
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero) return null;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return null;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return null;
        }

        /// <summary>当前进程是否已提权。</summary>
        public static bool IsCurrentProcessElevated()
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, (uint)p.Id);
                if (hProc == IntPtr.Zero) return false;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return false;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return false;
        }
    }
}
