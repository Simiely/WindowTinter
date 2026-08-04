using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowTinter
{
    /// <summary>
    /// 鎻愭潈妫€娴嬪伐鍏凤細鍒ゆ柇鐩爣杩涚▼ / 褰撳墠杩涚▼鏄惁浠ョ鐞嗗憳鏉冮檺杩愯銆?    /// 鍨簳鏂规闇€淇敼鐩爣绐楀彛鏍峰紡锛圵S_EX_LAYERED锛夛紝绠＄悊鍛樼洰鏍囧湪闈炴彁鏉冭繘绋嬩笅浼氶潤榛樺け璐モ€斺€?    /// 缁戝畾娴佺▼鎹缁欏嚭鎻愮ず銆?    /// </summary>
    internal static class Elevation
    {
        /// <summary>鐩爣绐楀彛鎵€鍦ㄨ繘绋嬫槸鍚﹀凡鎻愭潈銆傛棤娉曞垽瀹氭椂杩斿洖 null銆?/summary>
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

        /// <summary>褰撳墠杩涚▼鏄惁宸叉彁鏉冦€?/summary>
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

