using System.Windows;
using System.Windows.Input;

namespace WindowTinter.Views
{
    /// <summary>全局默认参数面板（设计稿 D）。DataContext = MainViewModel。</summary>
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
