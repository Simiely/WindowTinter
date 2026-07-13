using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// DWM Magnifier 暗化镜头：捕获桌面像素，DWM 合成暗化效果。
    /// 不参与常规 Z 序，不挡真实窗口。
    /// </summary>
    internal class DimLens : IDisposable
    {
        public IntPtr Handle { get; }
        private bool _visible;
        private static bool _magInited;

        // 缓存：避免每帧分配
        private readonly float[] _colorMatrix = new float[25];
        private Native.MAGCOLOREFFECT _cachedEffect;
        private Native.MAGTRANSFORM _transform = new() { v = new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 } };

        private const uint WS_POPUP = 0x80000000u;

        public DimLens()
        {
            if (!_magInited) { Native.MagInitialize(); _magInited = true; }

            Handle = Native.CreateWindowEx(
                Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE,
                "Magnifier", null, WS_POPUP,
                0, 0, 100, 100,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        public void Update(int alpha, Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            float scale = 1f - (alpha / 100f);
            if (scale < 0) scale = 0;

            // 移动窗口到目标位置
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | (_visible ? 0 : Native.SWP_SHOWWINDOW));

            // 1:1 变换
            Native.MagSetWindowTransform(Handle, ref _transform);
            Native.MagSetWindowSource(Handle, new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = h });

            // 复用缓存的颜色矩阵数组
            for (int i = 0; i < 3; i++)
            {
                _colorMatrix[i * 5 + i] = scale;
                _colorMatrix[i * 5 + 3] = 0; // alpha 列保持 0
            }
            _colorMatrix[18] = 1; // A row, A col = 1
            _colorMatrix[24] = 1; // O row, O col = 1

            _cachedEffect.transform = _colorMatrix;
            Native.MagSetColorEffect(Handle, ref _cachedEffect);
            Native.InvalidateRect(Handle, IntPtr.Zero, true);
            _visible = true;
        }

        public void Hide()
        {
            if (!_visible) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            _visible = false;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) Native.DestroyWindow(Handle);
        }
    }
}
