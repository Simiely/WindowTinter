using System.Windows;
using WindowTinter.ViewModels;

namespace WindowTinter.Views
{
    /// <summary>主窗口：DataContext = MainViewModel（真数据；阶段 C2 起不再用假数据）。</summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            // 关闭兜底：保存配置 + 全部还原释放（托盘拦截逻辑 D 阶段接入）
            Closing += (_, _) => _vm.Shutdown();
        }
    }
}
