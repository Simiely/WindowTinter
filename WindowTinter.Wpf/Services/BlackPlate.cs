using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WindowTinter
{
    /// <summary>
    /// 下层纯黑底板：紧贴目标窗口正后方的不透明纯黑分层窗口（纯 Win32 实现，脱离 WPF 布局系统）。
    /// 铁律对齐 WinForms 版 BlackPlate（逻辑原样搬）：
    /// - CreateWindowEx 创建时直接带 WS_EX_LAYERED，SetWindowPos / UpdateLayeredWindow / SetWindowRgn
    ///   全部用「物理像素」直控，坐标系一致，无需任何 DIP 换算；
    /// - 自定义 WndProc 吞掉 WM_DPICHANGED——框架（WinForms/WPF）会把 SetWindowPos 设定的物理像素坐标
    ///   按新旧 DPI 比例重算，导致黑底偏移/尺寸不对（v11 WPF Window 容器实测根因）。
    /// 圆角通过 SetWindowRgn + CreateRoundRectRgn（不依赖逐像素 Alpha），渲染用 UpdateLayeredWindow 不透明纯黑。
    /// </summary>
    internal class BlackPlate : IDisposable
    {
        private const int WM_DPICHANGED = 0x02E0;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const string ClassName = "WindowTinter.BlackPlate";

        // 委托必须静态持有，防止被 GC 回收导致 WndProc 失效
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static readonly WndProcDelegate _wndProc = WndProc;
        private static IntPtr _wndProcPtr;
        private static IntPtr _hInstance;
        private static bool _classRegistered;

        private IntPtr _hwnd = IntPtr.Zero;

        private Bitmap _cachedBmp;
        private int _cachedW, _cachedH, _cachedCornerRadius;
        private IntPtr _hBmp = IntPtr.Zero;   // 由 _cachedBmp 派生的 GDI 位图句柄，跨帧缓存减少句柄抖动

        private int _rgnW, _rgnH, _rgnRadius; // 窗口区域缓存（SetWindowRgn 传入后由系统管理）

        private IntPtr _lastTarget = IntPtr.Zero;
        private int _lastX, _lastY, _lastW, _lastH;

        /// <summary>圆角半径（0=关，矩形；1~20=px）。设值后下次 AlignBehind 生效（走 RefreshForeground 触发）。</summary>
        public int CornerRadius { get; set; } = 15;

        private static void RegisterClassIfNeeded()
        {
            if (_classRegistered) return;
            _hInstance = Native.GetModuleHandleW(null);
            _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);
            var wc = new Native.WNDCLASS
            {
                style = 0,
                lpfnWndProc = _wndProcPtr,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = _hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = ClassName
            };
            Native.RegisterClassW(ref wc);
            _classRegistered = true;
        }

        /// <summary>吞掉 WM_DPICHANGED：黑底由 SetWindowPos 物理像素直控，禁止框架按 DIP 重缩放。</summary>
        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DPICHANGED) return IntPtr.Zero;
            return Native.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        /// <summary>懒创建：注册窗口类 + CreateWindowEx（WS_POPUP + WS_EX_LAYERED + WS_EX_TOOLWINDOW，不置顶不穿透）。</summary>
        private void EnsureHandle()
        {
            if (_hwnd != IntPtr.Zero) return;
            RegisterClassIfNeeded();
            _hwnd = Native.CreateWindowExW(Native.WS_EX_LAYERED | WS_EX_TOOLWINDOW,
                ClassName, "WindowTinter.BlackPlate", WS_POPUP,
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        }

        /// <summary>
        /// 把底板钉到目标正后方（hWndInsertAfter = 目标句柄），并渲染不透明纯黑。
        /// 原型 B：每次调用都 SetWindowPos 维护"Z 序不变式"（黑底 = 目标紧邻下方）；
        /// 渲染（ULW/圆角）仅在几何变化时执行，避免无谓 GDI 开销。
        /// </summary>
        public void AlignBehind(IntPtr targetHandle, Native.RECT r)
        {
            int w = r.Width;
            int h = r.Height;
            if (w <= 0 || h <= 0) { HidePlate(); return; }
            int x = r.Left;
            int y = r.Top;

            EnsureHandle();
            if (_hwnd == IntPtr.Zero) return;

            bool posChanged = targetHandle != _lastTarget
                || x != _lastX || y != _lastY || w != _lastW || h != _lastH;

            // 关键：用目标句柄作为 hWndInsertAfter，使底板在 Z 序中紧挨目标之下。
            Native.SetWindowPos(_hwnd, targetHandle,
                x, y, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            if (posChanged)
                (_lastTarget, _lastX, _lastY, _lastW, _lastH) = (targetHandle, x, y, w, h);

            ApplyWindowRegion(w, h);

            if (posChanged)
                RenderSolidBlack(x, y, w, h);
        }

        /// <summary>隐藏底板（SWP_NOZORDER：隐藏时明确不改 Z 序）。</summary>
        public void HidePlate()
        {
            if (_hwnd != IntPtr.Zero)
            {
                Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_HIDEWINDOW | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
        }

        /// <summary>通过 SetWindowRgn 裁剪窗口形状。radius=0 恢复全矩形；radius&gt;0 圆角矩形。</summary>
        private void ApplyWindowRegion(int w, int h)
        {
            int r = CornerRadius;
            int clamped = r > 0 ? Math.Min(r, Math.Min(w / 2, h / 2)) : 0;

            if (w == _rgnW && h == _rgnH && clamped == _rgnRadius)
                return;

            IntPtr hrgn = clamped > 0
                ? Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, clamped, clamped)
                : Native.CreateRectRgn(0, 0, w, h);

            if (hrgn != IntPtr.Zero)
            {
                Native.SetWindowRgn(_hwnd, hrgn, true);
                (_rgnW, _rgnH, _rgnRadius) = (w, h, clamped); // 区域句柄所有权已移交系统，不手动释放
            }
        }

        private void RenderSolidBlack(int x, int y, int w, int h)
        {
            if (_cachedBmp == null || w != _cachedW || h != _cachedH || CornerRadius != _cachedCornerRadius)
            {
                if (_hBmp != IntPtr.Zero) { Native.DeleteObject(_hBmp); _hBmp = IntPtr.Zero; }
                _cachedBmp?.Dispose();
                _cachedBmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(_cachedBmp))
                    g.Clear(Color.Black);
                _cachedW = w; _cachedH = h;
                _cachedCornerRadius = CornerRadius;
            }

            IntPtr hdcScreen = Native.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return;

            IntPtr hdcMem = Native.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); return; }

            if (_hBmp == IntPtr.Zero)
                _hBmp = _cachedBmp.GetHbitmap();
            IntPtr hOld = IntPtr.Zero;
            try
            {
                hOld = Native.SelectObject(hdcMem, _hBmp);

                var ptDst = new Point(x, y);
                var ptSrc = new Point(0, 0);
                var sz = new Size(w, h);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255, // 不透明纯黑
                    AlphaFormat = 0            // 全局不透明（圆角由 SetWindowRgn 裁剪）
                };

                Native.UpdateLayeredWindow(_hwnd, hdcScreen,
                    ref ptDst, ref sz, hdcMem, ref ptSrc,
                    0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (hOld != IntPtr.Zero) Native.SelectObject(hdcMem, hOld);
                Native.DeleteDC(hdcMem);
                Native.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        public void Dispose()
        {
            if (_hBmp != IntPtr.Zero) { Native.DeleteObject(_hBmp); _hBmp = IntPtr.Zero; }
            _cachedBmp?.Dispose();
            try { HidePlate(); } catch { }
            if (_hwnd != IntPtr.Zero)
            {
                Native.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
