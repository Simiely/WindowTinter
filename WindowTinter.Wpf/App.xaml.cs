using System;
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
            var mutex = new Mutex(true, "WindowTinter.Wpf", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }
    }
}
