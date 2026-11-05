using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WindowTinter
{
    /// <summary>
    /// 窗口快照服务：用 PrintWindow 抓取目标窗口画面，缩放到卡片快照尺寸。
    /// 失败场景（UWP/受保护窗口/最小化/全黑结果）返回 null，由卡片显示占位。
    /// </summary>
    internal static class SnapshotService
    {
        /// <summary>抓取窗口快照并缩放到 maxW×maxH 内（保持宽高比、不放大）。失败返回 null，调用方负责 Dispose。</summary>
        public static Bitmap Capture(IntPtr hwnd, int maxW, int maxH)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
            if (!Native.GetWindowRect(hwnd, out Native.RECT r)) return null;
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) return null;

            double scale = Math.Min((double)maxW / w, (double)maxH / h);
            scale = Math.Min(scale, 1.0); // 不放大
            int tw = Math.Max((int)(w * scale), 1), th = Math.Max((int)(h * scale), 1);

            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
                if (!ok) return null;
            }
            if (IsBlank(bmp)) return null; // PrintWindow 对某些窗口返回全黑

            var result = new Bitmap(tw, th);
            using (var gg = Graphics.FromImage(result))
            {
                gg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                gg.DrawImage(bmp, 0, 0, tw, th);
            }
            return result;
        }

        /// <summary>中心区域采样，80%+ 纯黑视为抓图失败（常见于被保护/硬件加速渲染窗口）。</summary>
        private static bool IsBlank(Bitmap bmp)
        {
            int cx = bmp.Width / 2, cy = bmp.Height / 2;
            int samples = 0, black = 0;
            for (int x = Math.Max(0, cx - 30); x < Math.Min(bmp.Width, cx + 30) && samples < 40; x += 6)
                for (int y = Math.Max(0, cy - 30); y < Math.Min(bmp.Height, cy + 30) && samples < 40; y += 6)
                {
                    var p = bmp.GetPixel(x, y);
                    samples++;
                    if (p.R < 10 && p.G < 10 && p.B < 10) black++;
                }
            return samples > 0 && black * 10 > samples * 8;
        }
    }
}
