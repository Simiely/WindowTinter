using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 真·反色镜头（实验性）：利用 Windows Magnification API 的 MagSetColorEffect，
    /// 在放大镜窗口里对目标窗口区域套"反色"矩阵，覆盖在原窗口上。
    ///
    /// 注意：放大镜镜头有兼容性/性能怪癖，定位为实验模式；部分窗口下可能出现渲染瑕疵。
    ///
    /// 遮挡修复（v1.1.0）：放大镜窗口改为**顶层窗口**，并像蒙版一样用 SetWindowRgn
    /// 按"可见区域"裁剪——只反色百度网盘未被遮挡的部分，被其他窗口盖住的地方变透明，
    /// 露出下层真实（未反色）内容。这样上层窗口绝不会被误反色，且不再"整块消失"。
    /// </summary>
    internal class InvertLens : IDisposable
    {
        private IntPtr _hwndMag = IntPtr.Zero;
        private bool _initialized;

        private Native.RECT _lastRect;
        private bool _hasLast;

        private IntPtr _hrgn = IntPtr.Zero; // 当前生效的裁剪区域（本类负责释放）

        public bool Available => _hwndMag != IntPtr.Zero;

        /// <summary>本工具自身的窗口句柄（放大镜控件），供遮挡检测跳过。</summary>
        public IntPtr[] OwnHandles => new[] { _hwndMag };

        public void Start()
        {
            if (_initialized) return;
            if (!Native.MagInitialize()) return;

            // 顶层放大镜窗口（无父），用屏幕坐标定位，便于 SetWindowRgn 区域裁剪
            _hwndMag = Native.CreateWindowEx(
                Native.WS_EX_TOPMOST,
                "Magnifier",
                "WindowTinterMag",
                Native.WS_POPUP | Native.WS_VISIBLE,
                0, 0, 100, 100,
                IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwndMag == IntPtr.Zero)
            {
                Native.MagUninitialize();
                return;
            }

            // 1x 变换（单位矩阵）
            var t = new Native.MAGTRANSFORM { v = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 } };
            Native.MagSetWindowTransform(_hwndMag, ref t);

            // 反色矩阵：R'=1-R, G'=1-G, B'=1-B, A'=A
            var e = new Native.MAGCOLOREFFECT
            {
                transform = new float[25]
                {
                    -1, 0, 0, 0, 1,   // R' = -R + 1
                     0,-1, 0, 0, 1,   // G' = -G + 1
                     0, 0,-1, 0, 1,   // B' = -B + 1
                     0, 0, 0, 1, 0,   // A' =  A
                     0, 0, 0, 0, 1
                }
            };
            Native.MagSetColorEffect(_hwndMag, ref e);

            _initialized = true;
        }

        /// <summary>
        /// 把反色镜头对齐到目标矩形，并裁成可见区域 hrgn（屏幕坐标，本类接管并负责释放）。
        /// 仅在矩形真正变化时才重新抓屏，避免无谓的 MagSetWindowSource 开销。
        /// </summary>
        public void Update(Native.RECT r, IntPtr hrgn)
        {
            if (!Available)
            {
                if (hrgn != IntPtr.Zero) Native.DeleteObject(hrgn);
                return;
            }

            bool same = _hasLast
                && r.Left == _lastRect.Left && r.Top == _lastRect.Top
                && r.Right == _lastRect.Right && r.Bottom == _lastRect.Bottom;
            if (!same)
            {
                _lastRect = r;
                _hasLast = true;
                Native.MagSetWindowSource(_hwndMag, r);
                Native.SetWindowPos(
                    _hwndMag, Native.HWND_TOPMOST,
                    r.Left, r.Top, r.Width, r.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }

            // 区域裁剪：放大镜窗口只显示在可见区域，被盖住的部分透明（露出下层真实内容）
            if (hrgn == IntPtr.Zero)
            {
                if (_hrgn != IntPtr.Zero) { Native.DeleteObject(_hrgn); _hrgn = IntPtr.Zero; }
                Native.SetWindowRgn(_hwndMag, IntPtr.Zero, true);
            }
            else
            {
                Native.OffsetRgn(hrgn, -r.Left, -r.Top); // 屏幕坐标 -> 放大镜窗口坐标
                Native.SetWindowRgn(_hwndMag, hrgn, true);
                if (_hrgn != IntPtr.Zero) Native.DeleteObject(_hrgn);
                _hrgn = hrgn; // 接管所有权
            }
        }

        public void Hide()
        {
            if (_hwndMag != IntPtr.Zero)
                Native.SetWindowPos(_hwndMag, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_HIDEWINDOW | Native.SWP_NOACTIVATE);
        }

        public void Dispose()
        {
            if (_hrgn != IntPtr.Zero) { Native.DeleteObject(_hrgn); _hrgn = IntPtr.Zero; }
            if (_hwndMag != IntPtr.Zero)
            {
                Native.DestroyWindow(_hwndMag);
                _hwndMag = IntPtr.Zero;
            }
            if (_initialized)
            {
                Native.MagUninitialize();
                _initialized = false;
            }
        }
    }
}
