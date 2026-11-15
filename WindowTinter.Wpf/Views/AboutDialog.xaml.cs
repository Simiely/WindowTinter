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
            var ver = typeof(AboutDialog).Assembly.GetName().Version;
            VersionText.Text = ver != null
                ? $"暗幕 v{ver.Major}.{ver.Minor}.{ver.Build}"
                : "暗幕";
            WindowTheme.EnableDarkTitleBar(this);
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
