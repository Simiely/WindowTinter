using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WindowTinter.Views;

namespace WindowTinter
{
    public partial class App : Application
    {
        // 必须持有引用：Mutex 被 GC 回收等于放弃单实例占位，第二个实例可趁虚而入
        private Mutex _singleInstanceMutex;

        // 第二实例唤起第一实例：命名事件（第一实例后台等待，收到后回投 UI 线程显示主窗口）
        private const string ShowWindowEventName = "WindowTinter.Wpf.ShowWindow";
        private EventWaitHandle _showWindowEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 必须在任何窗口创建前：启用进程级深色模式（决定 Win32 原生托盘菜单是否跟随系统深色）
            Native.EnableDarkMenus();

            base.OnStartup(e);

            // 开机自启（注册表 Run 项带 /startup）时只驻留托盘、不显示主窗口；
            // 手动双击 exe 不带参数，则正常显示主窗口。
            bool startHidden = e.Args.Any(a =>
                a.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/background", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

            _singleInstanceMutex = new Mutex(true, "WindowTinter.Wpf", out bool createdNew);
            if (!createdNew)
            {
                // 已有实例：手动启动则请求它显示主窗口；静默启动（如重复的自启项）直接退出，不打扰用户
                if (!startHidden) SignalShowWindow();
                Shutdown();
                return;
            }

            StartShowWindowListener();

            // 静默启动：创建窗口（含托盘）但不 Show，效果由 MainViewModel 自行绑定生效
            var win = new MainWindow(startHidden);
            MainWindow = win;
            if (!startHidden) win.Show();
        }

        /// <summary>通知已运行的实例显示主窗口；第一实例尚未建好事件时静默放弃。</summary>
        private static void SignalShowWindow()
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShowWindowEventName);
                evt.Set();
            }
            catch { /* 第一实例还没创建事件 / 正在退出：忽略 */ }
        }

        /// <summary>后台等待唤起信号，收到后回投 UI 线程显示主窗口（不依赖主窗口是否已显示）。</summary>
        private void StartShowWindowListener()
        {
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    if (!_showWindowEvent.WaitOne()) break;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(
                            () => (MainWindow as WindowTinter.Views.MainWindow)?.ShowFromSecondInstance()));
                    }
                    catch { /* 应用正在退出 */ }
                }
            })
            { IsBackground = true, Name = "WindowTinter.ShowWindowListener" };
            listener.Start();
        }
    }
}
