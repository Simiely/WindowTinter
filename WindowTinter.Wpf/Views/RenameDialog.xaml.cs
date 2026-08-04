using System.Windows;

namespace WindowTinter.Views
{
    /// <summary>
    /// 重命名对话框（WPF 版，对应 WinForms RenameDialog.cs）：
    /// 确定 → DialogResult=true + RenameText；取消/关闭 → false。
    /// 输入为空 = 清除别名（恢复默认显示名）。
    /// </summary>
    public partial class RenameDialog : Window
    {
        public string RenameText => Input.Text;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            Input.Text = currentName ?? "";
            Loaded += (_, _) => { Input.SelectAll(); Input.Focus(); };
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
