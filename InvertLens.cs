using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 真·反色镜头（实验性）：利用 Windows Magnification API 的 MagSetColorEffect，
    /// 在 1x 放大镜窗口里对目标窗口区域套"反色"矩阵，覆盖在原窗口上。
    ///
    /// 注意：放大镜镜头有兼容性/性能怪癖，定位为实验模式；部分窗口下可能出现渲染瑕疵。
    /// </summary>
    internal class InvertLens : IDisposable
    {
        private IntPtr _hwndMag = IntPtr.Zero;
        private Form _host; // 隐藏宿主，仅用于持有放大镜子窗口的句柄
        private bool _initialized;

        public bool Available => _hwndMag != IntPtr.Zero;

        public void Start()
        {
            if (_initialized) return;
            if (!Native.MagInitialize()) return;

            _host = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                Width = 10,
                Height = 10
            };
            _host.Show();
            _host.Hide(); // 仅用于拿到句柄

            _hwndMag = Native.CreateWindowEx(
                0,
                "Magnifier",
                "WindowTinterMag",
                Native.WS_CHILD | Native.WS_VISIBLE,
                0, 0, 100, 100,
                _host.Handle,
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

        /// <summary>把反色镜头对齐到目标矩形并刷新源区域。</summary>
        public void Update(Native.RECT r)
        {
            if (!Available) return;
            Native.MagSetWindowSource(_hwndMag, r);
            Native.SetWindowPos(
                _hwndMag, Native.HWND_TOPMOST,
                r.Left, r.Top, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void Hide()
        {
            if (_hwndMag != IntPtr.Zero)
                Native.SetWindowPos(_hwndMag, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_HIDEWINDOW | Native.SWP_NOACTIVATE);
        }

        public void Dispose()
        {
            if (_hwndMag != IntPtr.Zero)
            {
                Native.DestroyWindow(_hwndMag);
                _hwndMag = IntPtr.Zero;
            }
            _host?.Close();
            if (_initialized)
            {
                Native.MagUninitialize();
                _initialized = false;
            }
        }
    }
}
