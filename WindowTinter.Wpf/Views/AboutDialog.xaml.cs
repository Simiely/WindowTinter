using System.Diagnostics;
using System.Windows;

namespace WindowTinter.Views
{
    /// <summary>关于对话框（WPF 设计语言：深色圆角卡片 + 主色调按钮，替代原 MessageBox）。</summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnOpenGitHub(object sender, RoutedEventArgs e)
        {
            try { Process.Start("https://github.com/Simiely/WindowTinter"); } catch { }
        }
    }
}
