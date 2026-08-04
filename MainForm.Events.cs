using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 系统事件层：窗体关闭拦截 + WinEvent 全局钩子 + 运行时 DPI 切换自适应。
    /// 外部窗口事件 → 领域逻辑（ReleaseTarget / RefreshNow）的桥接。
    /// </summary>
    internal partial class MainForm
    {
        private const int WM_DPICHANGED = 0x02E0;

        /// <summary>
        /// 运行时 DPI 切换（窗口拖到不同缩放显示器 / 系统缩放设置变化）自适应。
        /// WinForms AutoScaleMode.Dpi 只在窗口创建时缩放一次，不响应 WM_DPICHANGED 重排；
        /// 这里拦截后：应用系统建议的新窗口矩形 → 按 newScale/oldScale 比例缩放全部控件
        /// → 更新 _dpiScale → 重建目标列表（面板宽按新 DPI）。
        /// _dpiReady 防初始创建时（OnLoad 前，AutoScaleMode 已处理初始缩放）误触发双重缩放。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED && _dpiReady)
            {
                try
                {
                    uint newDpi = Native.GetDpiForWindow(Handle);
                    float newScale = Math.Max(newDpi / 96f, 1f);
                    if (Math.Abs(newScale - _dpiScale) > 0.001f)
                    {
                        float factor = newScale / _dpiScale;
                        var rect = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));

                        // 1) 应用系统建议的新窗口矩形（物理像素）
                        if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                        {
                            Native.SetWindowPos(Handle, IntPtr.Zero,
                                rect.Left, rect.Top,
                                rect.Right - rect.Left, rect.Bottom - rect.Top,
                                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                        }

                        // 2) 按比例缩放全部控件（位置/尺寸/字体）
                        ScaleAllControls(this, factor);

                        // 3) 更新 DPI 基准并重建目标列表（面板宽度按新 _dpiScale）
                        _dpiScale = newScale;
                        RebuildTargetList();
                        UpdateUI();
                    }
                }
                catch { }
                return; // 已自行处理，吞掉默认
            }
            base.WndProc(ref m);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            { _settings.Save(); e.Cancel = true; Hide(); ShowInTaskbar = false; }
        }

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshForeground(); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            // 目标特定事件：在 BeginInvoke 内读取 _entries，避免跨线程访问非安全集合
            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_REORDER
                               or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                try { BeginInvoke(new Action(() =>
                {
                    var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                    if (match == null) return;
                    if (eventType == Native.EVENT_OBJECT_DESTROY)
                        ReleaseTarget(match, "destroyed"); // 事件驱动即时迁移：销毁→待激活
                    else
                        match.Tracker.RefreshNow(); // 含 REORDER：触发 OnUpdate → 重插黑底维护 Z 序不变式
                })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
