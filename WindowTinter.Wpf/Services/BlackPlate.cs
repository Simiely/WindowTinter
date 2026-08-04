using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowTinter
{
    /// <summary>
    /// 黑底垫底窗口（WPF 版）：透明 Window + 黑色圆角 Border，通过 SetWindowPos 插入到目标窗口"正下方"维护 Z 序。
    /// 原型 B：每次调用都 SetWindowPos 重插 Z 序不变式；渲染（ULW/圆角）仅在几何变化时执行，避免无谓 GDI 开销。
    /// </summary>
    internal class BlackPlate : Window
    {
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int SWP_HIDEWINDOW = 0x0080;

        private int _cornerRadius = 15;
        private IntPtr _lastTarget = IntPtr.Zero;
        private int _lastX, _lastY, _lastW, _lastH;

        private readonly Border _border;

        public BlackPlate()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Focusable = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            _border = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(_cornerRadius) };
            Content = _border;
        }

        /// <summary>圆角半径（0=直角，1-20=px），设值时立即更新黑底 Border。</summary>
        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = value;
                if (_border != null) _border.CornerRadius = new CornerRadius(value);
            }
        }

        /// <summary>把黑底钉到目标正下方，并渲染不透明纯黑。</summary>
        public void AlignBehind(IntPtr targetHandle, Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { HidePlate(); return; }
            if (!IsVisible) Show();

            // 窗口居中置顶（SetWindowPos 的 hWndInsertAfter = target → 紧贴目标下面）
            // 注意：WPF 透明窗口的 Z 序可由 SetWindowPos 直接控制
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) { Show(); hwnd = new WindowInteropHelper(this).Handle; }
            Native.SetWindowPos(hwnd, targetHandle,
                r.Left, r.Top, w, h,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);

            _lastTarget = targetHandle;
            _lastX = r.Left; _lastY = r.Top; _lastW = w; _lastH = h;
        }

        /// <summary>隐藏黑底。</summary>
        public void HidePlate() { if (IsVisible) Hide(); }
    }
}
