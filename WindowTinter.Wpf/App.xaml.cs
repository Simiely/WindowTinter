using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WindowTinter.Views;

namespace WindowTinter
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "WindowTinter.Wpf";
        /// <summary>二次启动通知信号（Local=当前会话；与互斥锁配套：已有实例监听，新实例触发）。</summary>
        private const string SecondLaunchSignalName = @"Local\WindowTinter.Wpf.OpenSignal";

        /// <summary>UI 线程 Dispatcher（OnStartup 中捕获）。后台监听线程经它 BeginInvoke——
        /// 不能从后台线程直接访问 Application.Current.Dispatcher（VerifyAccess 会抛异常）。</summary>
        private Dispatcher _uiDispatcher;

        /// <summary>主窗口引用（UI 线程写入；后台线程只读引用字段——引用读取原子，无需跨线程访问 Application）。</summary>
        private Window _mainWindow;

        private Mutex _mutex;
        private EventWaitHandle _secondLaunchSignal;
        private Thread _signalListenerThread;

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

            // UI 线程捕获 Dispatcher（供后台信号监听线程 BeginInvoke，跨线程安全）
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            _mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // 已有实例在运行：触发"显示主窗口"信号后退出（标准单实例行为：
                // 再次启动 = 打开窗口，而非静默无反应）。信号方式不依赖窗口句柄——
                // 静默驻留时主窗口句柄根本不存在，SetForegroundWindow(MainWindowHandle)
                // 类方案在此架构下不可行；由已有实例的 Dispatcher 直接调用窗口方法。
                try
                {
                    using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, SecondLaunchSignalName);
                    signal.Set();
                }
                catch { /* 信号被占用/权限异常时静默退出，不影响已有实例 */ }
                Shutdown();
                return;
            }

            ListenForSecondLaunch();

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
            _mainWindow = win; // 供后台监听线程读取（引用读取原子，避免跨线程访问 Application.MainWindow）
            if (!startHidden) win.Show();
        }

        /// <summary>
        /// 监听二次启动信号：收到后让主窗口显示到前台。
        /// 后台线程阻塞在 WaitOne（消息循环空闲时近乎零开销），通过 Dispatcher 回 UI 线程执行。
        /// </summary>
        private void ListenForSecondLaunch()
        {
            try { _secondLaunchSignal = new EventWaitHandle(false, EventResetMode.ManualReset, SecondLaunchSignalName); }
            catch { return; } // 信号创建失败（极少见）则退化为无二次启动支持

            _signalListenerThread = new Thread(() =>
            {
                while (_secondLaunchSignal != null)
                {
                    try
                    {
                        _secondLaunchSignal.WaitOne();
                        _secondLaunchSignal.Reset();
                    }
                    catch { break; } // 信号已释放（应用退出）
                    var win = _mainWindow as MainWindow; // 引用读取原子安全；不要访问 Application.MainWindow（跨线程会 VerifyAccess 抛异常）
                    if (win == null) continue;
                    _uiDispatcher?.BeginInvoke(new Action(win.ShowFromSecondInstance));
                }
            })
            { IsBackground = true };
            _signalListenerThread.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _signalListenerThread = null;
            try { _secondLaunchSignal?.Dispose(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
