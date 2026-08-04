using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WindowTinter.ViewModels;

namespace WindowTinter.Views
{
    /// <summary>主窗口（阶段 B：静态 UI 1:1，假数据展示卡片网格）。</summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = FakeData.Create();
        }
    }
}
