using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowTinter.Views
{
    /// <summary>
    /// 窗口拾取器（WPF 版，对齐设计稿 C）：
    /// 全屏 scrim + 目标蓝描边四角标 + 左侧 HUD（宽 304，与目标窗重叠时自动换到右侧）。
    /// 内部命中逻辑（GetWindowAtCursor / IsIgnoredWindow / FallbackEnumerate / 30ms 节流）原样保留。
    /// 左键点击窗口选定，右键 / Esc / HUD「取消」取消。
    /// </summary>
    public partial class WindowPickerWindow : Window
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private IntPtr _selfHwnd = IntPtr.Zero;
        private DateTime _lastMoveCheck = DateTime.MinValue;

        public WindowPickerWindow()
        {
            InitializeComponent();
            // 覆盖虚拟屏幕（WPF 按 DIP 定位）
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Loaded += (_, _) =>
            {
                Focus();
                InitScrim();
                PositionHud(null);
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _selfHwnd = new WindowInteropHelper(this).Handle;
            Activate();
        }

        /// <summary>
        /// 命中测试：优先用 WindowFromPoint（系统级命中测试，尊重 Z 序、子窗口与 WS_EX_TRANSPARENT 语义）。
        /// 若命中自己 / 桌面 / 任务栏等"不可交互"窗口，退回 Z 序枚举。
        /// 光标位置直接问系统（GetCursorPos，物理像素），不依赖 WPF 路由事件的坐标换算。
        /// </summary>
        private IntPtr GetWindowAtCursor()
        {
            if (!Native.GetCursorPos(out Native.POINT p)) return IntPtr.Zero;
            var spt = new System.Drawing.Point(p.X, p.Y);
            IntPtr h = Native.WindowFromPoint(spt);

            if (h == IntPtr.Zero || h == _selfHwnd || IsIgnoredWindow(h))
                h = FallbackEnumerate(spt);

            return h != IntPtr.Zero ? Native.GetAncestor(h, Native.GA_ROOT) : IntPtr.Zero;
        }

        /// <summary>该窗口是否属于"不该被选中"的类型：自身、不可见、最小化、桌面/任务栏、透明覆盖层。</summary>
        private static bool IsIgnoredWindow(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;

            string cls = GetClass(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            return (ex & Native.WS_EX_TRANSPARENT) != 0;
        }

        private static string GetClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetTitle(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>兜底：EnumWindows 按 Z 序从顶到底枚举，取第一个"包含光标 + 可交互"的顶层窗口。</summary>
        private IntPtr FallbackEnumerate(System.Drawing.Point pt)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (hwnd == _selfHwnd || IsIgnoredWindow(hwnd)) return true;

                Native.GetWindowRect(hwnd, out Native.RECT r);
                if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            // 30ms 节流——避免每像素移动都 EnumWindows
            var now = DateTime.UtcNow;
            if ((now - _lastMoveCheck).TotalMilliseconds < 30) return;
            _lastMoveCheck = now;

            IntPtr h = GetWindowAtCursor();
            if (h == _lastHwnd) return;

            _lastHwnd = h;

            if (h != IntPtr.Zero && Native.GetWindowRect(h, out _highlight))
                DrawHighlight(h, _highlight);
            else
                ClearHighlight();
        }

        private void OnMouseClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                CancelPicker();
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;

            // 点在 HUD 上（按钮以外的空白）不当作"选中下面的窗口"
            var pos = e.GetPosition(this);
            double hudLeft = Canvas.GetLeft(Hud), hudTop = Canvas.GetTop(Hud);
            if (pos.X >= hudLeft && pos.X <= hudLeft + Hud.ActualWidth &&
                pos.Y >= hudTop && pos.Y <= hudTop + Hud.ActualHeight) return;

            IntPtr h = GetWindowAtCursor();
            if (h != IntPtr.Zero)
            {
                SelectedHandle = h;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelPicker();
                e.Handled = true;
            }
        }

        private void OnCancelHud(object sender, RoutedEventArgs e) => CancelPicker();

        private void OnConfirmHud(object sender, RoutedEventArgs e)
        {
            if (_lastHwnd == IntPtr.Zero) return;
            SelectedHandle = _lastHwnd;
            DialogResult = true;
            Close();
        }

        private void CancelPicker()
        {
            ClearHighlight();
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ClearHighlight();
            base.OnClosing(e);
        }

        // ── 覆盖层绘制（WPF 视觉，不再用 PatBlt 反转）──

        private void DrawHighlight(IntPtr hwnd, Native.RECT r)
        {
            var rect = ToDip(r);
            TargetLabelText.Text = GetTitle(hwnd) is { Length: > 0 } t ? (t.Length > 42 ? t.Substring(0, 42) + "…" : t) : GetClass(hwnd);
            TargetLabelSize.Text = $"{r.Width} × {r.Height}";
            HudTitle.Text = TargetLabelText.Text;
            HudMeta.Text = $"{r.Width} × {r.Height} px";
            HudDot.Fill = (Brush)FindResource("Accent");
            ConfirmButton.IsEnabled = true;

            ApplyScrim(rect);
            Canvas.SetLeft(FrameVisual, rect.X);
            Canvas.SetTop(FrameVisual, rect.Y);
            FrameVisual.Width = Math.Max(rect.Width, 8);
            FrameVisual.Height = Math.Max(rect.Height, 8);
            TargetFrame.Visibility = Visibility.Visible;
            PositionHud(rect);
        }

        /// <summary>物理像素矩形 → 本窗口 DIP 坐标。</summary>
        private Rect ToDip(Native.RECT r)
        {
            Point tl = PointFromScreen(new Point(r.Left, r.Top));
            Point br = PointFromScreen(new Point(r.Right, r.Bottom));
            return new Rect(tl, br);
        }

        /// <summary>初始遮罩：整屏压暗（未命中目标前没有"挖空"区域）。</summary>
        private void InitScrim()
        {
            double w = ActualWidth;
            double h = ActualHeight;

            Canvas.SetLeft(TopScrim, 0); Canvas.SetTop(TopScrim, 0);
            TopScrim.Width = 0; TopScrim.Height = 0;

            Canvas.SetLeft(LeftScrim, 0); Canvas.SetTop(LeftScrim, 0);
            LeftScrim.Width = 0; LeftScrim.Height = 0;

            Canvas.SetLeft(RightScrim, w); Canvas.SetTop(RightScrim, 0);
            RightScrim.Width = 0; RightScrim.Height = 0;

            Canvas.SetLeft(BottomScrim, 0); Canvas.SetTop(BottomScrim, 0);
            BottomScrim.Width = w; BottomScrim.Height = h;
        }

        private void ApplyScrim(Rect r)
        {
            double w = ActualWidth;
            double h = ActualHeight;

            Canvas.SetLeft(TopScrim, 0); Canvas.SetTop(TopScrim, 0);
            TopScrim.Width = w; TopScrim.Height = Math.Max(0, r.Top);

            Canvas.SetLeft(BottomScrim, 0); Canvas.SetTop(BottomScrim, r.Bottom);
            BottomScrim.Width = w; BottomScrim.Height = Math.Max(0, h - r.Bottom);

            Canvas.SetLeft(LeftScrim, 0); Canvas.SetTop(LeftScrim, r.Top);
            LeftScrim.Width = Math.Max(0, r.Left); LeftScrim.Height = Math.Max(0, r.Height);

            Canvas.SetLeft(RightScrim, r.Right); Canvas.SetTop(RightScrim, r.Top);
            RightScrim.Width = Math.Max(0, w - r.Right); RightScrim.Height = Math.Max(0, r.Height);
        }

        /// <summary>HUD 定位：默认左上 16/32；与目标矩形相交时换到右侧，保持不遮挡目标。</summary>
        private void PositionHud(Rect? target)
        {
            const double margin = 16;
            const double top = 32;

            if (!IsLoaded) return;

            double hudW = Hud.Width;
            Hud.Measure(new Size(hudW, double.PositiveInfinity));
            double hudH = Hud.DesiredSize.Height > 0 ? Hud.DesiredSize.Height : 360;

            double left = margin;
            if (target.HasValue)
            {
                var targetRect = target.Value;
                var onLeft = new Rect(margin, top, hudW, hudH);
                if (onLeft.IntersectsWith(targetRect))
                {
                    var onRight = new Rect(Math.Max(margin, ActualWidth - hudW - margin), top, hudW, hudH);
                    if (!onRight.IntersectsWith(targetRect)) left = onRight.Left;
                }
            }

            Canvas.SetLeft(Hud, left);
            Canvas.SetTop(Hud, top);
        }

        private void ClearHighlight()
        {
            _lastHwnd = IntPtr.Zero;
            TargetFrame.Visibility = Visibility.Collapsed;
            HudTitle.Text = "移动鼠标选择目标";
            HudMeta.Text = "尚未命中任何窗口";
            HudDot.Fill = (Brush)FindResource("Off");
            ConfirmButton.IsEnabled = false;
            PositionHud(null);
        }
    }
}
