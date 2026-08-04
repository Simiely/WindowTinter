using System;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 系统事件层：窗体关闭拦截 + WinEvent 全局钩子 + 运行时 DPI 切换自适应。
    /// 外部窗口事件 → 领域逻辑（ReleaseTarget / RefreshNow）的桥接。
    /// </summary>
    internal partial class MainForm
    {
        /// <summary>
        /// 运行时 DPI 切换（窗口拖到不同缩放显示器 / 系统缩放设置变化）自适应。
        /// 用 .NET 6 内置 DpiChanged 事件（框架已正确解析 WM_DPICHANGED 并提供
        /// DeviceDpiNew/DeviceDpiOld）——比手写 WndProc 解析 wParam 可靠：
        /// - v7 曾用 GetDpiForWindow 取 DPI，但消息处理时 DPI 上下文未切换、返回旧值，
        ///   导致 newScale 计算错误、整个自适应被跳过（UI 不重排）——这就是 v7 的 bug。
        /// - 容器化重构后：根 TableLayoutPanel 按行高 Percent 自动重排（DPI 变化/窗口拉高时
        ///   目标卡自动占满剩余空间），框架已自动缩放静态控件——不再需要手动缩放控件树，
        ///   彻底消除 v7/v8 的双重缩放问题。此处只更新 _dpiScale 供动态面板换算。
        /// 窗口大小由框架按 SuggestedRectangle 自行调整，不重复 SetWindowPos。
        /// </summary>
        private void OnDpiChanged(object sender, DpiChangedEventArgs e)
        {
            if (!_dpiReady) return;
            try
            {
                float newScale = Math.Max(e.DeviceDpiNew / 96f, 1f);
                float factor = newScale / _dpiScale;
                if (Math.Abs(factor - 1f) < 0.001f) return;

                _dpiScale = newScale;
                RebuildTargetList(); // 目标卡片宽度按新 _dpiScale 重建
                UpdateUI();
            }
            catch { }
        }

        /// <summary>在窗体构造中挂接 DPI 变化事件（初始缩放由 AutoScaleMode.Dpi 完成，_dpiReady 前不响应）。</summary>
        private void HookDpiChanged() => DpiChanged += OnDpiChanged;

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
                    {
                        match.Tracker.RefreshNow(); // 含 REORDER：触发 OnUpdate → 重插黑底维护 Z 序不变式
                        if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_SHOW)
                            RefreshTargetSnapshot(match.Info); // 移动/显示时即时刷新卡片快照
                    }
                })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
