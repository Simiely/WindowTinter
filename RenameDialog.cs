using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 重命名对话框：输入自定义别名。
    /// 确定 → DialogResult.OK + RenameText；取消/关闭 → DialogResult.Cancel。
    /// 输入为空 = 清除别名（恢复默认显示名）。
    /// </summary>
    internal class RenameDialog : Form
    {
        private readonly TextBox _input;

        /// <summary>确定后返回用户输入的文本（可能为空字符串 = 清除别名）。</summary>
        public string RenameText => _input.Text;

        public RenameDialog(string currentName)
        {
            Text = "重命名窗口";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 110);
            BackColor = Color.FromArgb(40, 40, 40);
            ForeColor = Color.FromArgb(224, 224, 224);

            var lbl = new Label
            {
                Text = "显示名称（留空恢复默认）：",
                Location = new Point(12, 12),
                AutoSize = true,
                BackColor = BackColor,
                ForeColor = ForeColor
            };
            Controls.Add(lbl);

            _input = new TextBox
            {
                Location = new Point(12, 36),
                Size = new Size(316, 24),
                Text = currentName,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = ForeColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_input);

            var btnOk = new Button { Text = "确定", Location = new Point(172, 70), Size = new Size(75, 30), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "取消", Location = new Point(253, 70), Size = new Size(75, 30), DialogResult = DialogResult.Cancel };
            btnOk.BackColor = Color.FromArgb(60, 60, 60); btnOk.ForeColor = ForeColor; btnOk.FlatStyle = FlatStyle.Flat;
            btnCancel.BackColor = Color.FromArgb(60, 60, 60); btnCancel.ForeColor = ForeColor; btnCancel.FlatStyle = FlatStyle.Flat;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Shown += (_, _) => { _input.SelectAll(); _input.Focus(); };
        }
    }
}
