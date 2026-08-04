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
