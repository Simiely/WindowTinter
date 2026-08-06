using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowTinter
{
    /// <summary>System.Drawing.Bitmap → 已 Freeze 的 BitmapSource（跨线程安全）。快照路径唯一转换入口。</summary>
    internal static class ImageConvert
    {
        /// <summary>转换并释放源位图；null 输入返回 null（调用方按需处理）。</summary>
        public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
        {
            if (bmp == null) return null;
            try
            {
                IntPtr hbit = bmp.GetHbitmap();
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(
                        hbit, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze(); // Freeze 后跨线程安全
                    return src;
                }
                finally { Native.DeleteObject(hbit); }
            }
            catch { return null; }
            finally { bmp.Dispose(); }
        }
    }
}
