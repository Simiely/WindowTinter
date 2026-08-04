using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// DPI 缩放工具（独立模块，无状态）：运行时 DPI 切换时按比例递归缩放控件树。
    /// 与 MainForm 解耦——任何窗体/控件树都能复用。
    /// 
    /// 使用背景（2026-08 调研确认）：
    /// - WinForms AutoScaleMode.Dpi 只在窗口创建时按初始 DPI 缩放一次；
    ///   运行时跨 DPI 显示器移动（PMv2）时框架不会自动缩放控件（微软官方方案需手动 Scale）。
    /// - .NET 6 的 DpiChanged 事件提供正确的新旧 DPI（DeviceDpiNew/DeviceDpiOld），
    ///   用它算比例因子，避免手写 WM_DPICHANGED 解析 wParam 的坑。
    /// </summary>
    internal static class DpiScaling
    {
        /// <summary>
        /// 递归按比例缩放控件（位置/尺寸/字体）。
        /// AutoSize 控件只缩放位置与字体（尺寸由内容自动决定）；
        /// 非 AutoSize 控件（Button/GroupBox/Panel 等）同时缩放尺寸。
        /// 字体下限 6f 防止缩放过度导致不可读。
        /// </summary>
        public static void ScaleControlTree(Control root, float factor)
        {
            foreach (Control c in root.Controls)
            {
                c.Location = new Point((int)Math.Round(c.Left * factor), (int)Math.Round(c.Top * factor));
                if (!c.AutoSize)
                    c.Size = new Size((int)Math.Round(c.Width * factor), (int)Math.Round(c.Height * factor));
                if (c.Font != null)
                    c.Font = new Font(c.Font.FontFamily, Math.Max(c.Font.Size * factor, 6f), c.Font.Style);
                if (c.Controls.Count > 0)
                    ScaleControlTree(c, factor);
            }
        }
    }
}
