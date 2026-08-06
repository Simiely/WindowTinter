using System;
using System.Linq;
using System.Windows;

namespace WindowTinter.ViewModels
{
    /// <summary>MainViewModel partial：WinEvent 全局钩子（前台/移动/销毁即时响应）。</summary>
    internal partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // WinEvent 全局钩子（原样搬自 MainForm.Events.cs，BeginInvoke→Dispatcher）
        // ════════════════════════════════════════════════════════════════

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
            var app = Application.Current;
            if (app == null) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var e in _entries) e.Tracker.RefreshForeground();
                    }));
                }
                catch { }
                return;
            }

            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_REORDER
                               or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                        if (match == null) return;
                        if (eventType == Native.EVENT_OBJECT_DESTROY)
                            ReleaseTarget(match, "destroyed");
                        else
                        {
                            match.Tracker.RefreshNow();
                            // 目标窗口显示/启动 → 自动快照（用户运行时新启动的目标首次可见即有图）
                            if (eventType == Native.EVENT_OBJECT_SHOW)
                                RefreshTargetSnapshot(match.Info);
                        }
                    }));
                }
                catch { }
            }
        }
    }
}
