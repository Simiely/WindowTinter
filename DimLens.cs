using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// DWM Magnifier 暗化镜头：捕获目标窗口像素，DWM 内部合成暗化效果。
    /// 不使用 Z 序层叠，不会盖错上层窗口。
    /// </summary>
    internal class DimLens : IDisposable
    {
        private readonly IntPtr _hwnd;
        public IntPtr Handle => _hwnd;
        private float _scale = 0.25f;   // 1 - alpha%, 默认 25% 亮度 (75% 暗)
        private bool _visible;
        private static bool _magInited;

        public DimLens()
        {
            if (!_magInited)
            {
                Native.MagInitialize();
                _magInited = true;
            }

            _hwnd = CreateWindow(
                "Magnifier", 0, 0, 100, 100,
                Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE);

            // 初始隐藏
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        }

        public void Update(int alpha, Native.RECT targetRect)
        {
            int w = targetRect.Width, h = targetRect.Height;
            if (w <= 0 || h <= 0) { Hide(); return; }

            _scale = 1.0f - (alpha / 100.0f);
            if (_scale < 0) _scale = 0;

            // 移动窗口到目标位置
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST,
                targetRect.Left, targetRect.Top, w, h,
                Native.SWP_NOACTIVATE | (_visible ? 0 : Native.SWP_SHOWWINDOW));

            // 设置 Captor 源区域和目标窗口同尺寸
            var src = new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            Native.MagSetWindowSource(_hwnd, src);

            // 设置 1:1 变换（不缩放）
            var transform = new Native.MAGTRANSFORM
            {
                v = new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }
            };
            Native.MagSetWindowTransform(_hwnd, ref transform);

            // 设置暗化颜色矩阵
            var effect = new Native.MAGCOLOREFFECT
            {
                transform = new float[]
                {
                    _scale, 0,      0,      0, 0,
                    0,      _scale, 0,      0, 0,
                    0,      0,      _scale, 0, 0,
                    0,      0,      0,      1, 0,
                    0,      0,      0,      0, 1
                }
            };
            Native.MagSetColorEffect(_hwnd, ref effect);

            // 刷新
            Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
            _visible = true;
        }

        public void Hide()
        {
            if (_visible)
            {
                Native.ShowWindow(_hwnd, Native.SW_HIDE);
                _visible = false;
            }
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
                Native.DestroyWindow(_hwnd);
        }

        // -- NativeHelpers --

        private const uint WS_POPUP = 0x80000000u;

        private static IntPtr CreateWindow(string className, int x, int y, int w, int h, uint exStyle)
        {
            return Native.CreateWindowEx(
                exStyle,                    // dwExStyle
                className,                   // lpClassName (junk — not registered)
                null,                        // lpWindowName
                WS_POPUP,                   // dwStyle
                x, y, w, h,
                IntPtr.Zero,                // hWndParent
                IntPtr.Zero,                // hMenu
                IntPtr.Zero,                // hInstance
                IntPtr.Zero);               // lpParam
        }
    }
}
