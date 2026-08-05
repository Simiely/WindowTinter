using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WindowTinter.Views;

namespace WindowTinter
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 必须在任何窗口创建前：启用进程级深色模式（决定 Win32 原生托盘菜单是否跟随系统深色）
            Native.EnableDarkMenus();

            base.OnStartup(e);

            // 静默启动（/startup）时不显示任何窗口——默认 OnLastWindowClose 下，
            // "已显示窗口集合"为空会被判定为"最后窗口已关闭"而自动退出。
            // 显式改为 OnExplicitShutdown：应用只在我们显式调用 Shutdown() 时退出，
            // 这是 WPF 托盘应用（窗口按需显示）的标准配置。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mutex = new Mutex(true, "WindowTinter.Wpf", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            // 开机自启（注册表 Run 项带 /startup 参数）时只驻留托盘、不弹主窗口；
            // 手动双击 exe 不带参数则正常显示主窗口。静默判断与 WinForms 版保持一致。
            bool startHidden = e.Args.Any(a =>
                a.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/background", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

            // 静默启动：只创建窗口对象（构造函数已建好托盘），**绝不调用 Show()**——
            // 窗口句柄完全不创建，连一帧都不会渲染（官方推荐做法，杜绝任何闪烁）。
            // 用户双击托盘时才 Show() 首次创建句柄并恢复上次窗口位置。
            var win = new MainWindow();
            MainWindow = win;
            if (!startHidden) win.Show();
        }
    }
}
