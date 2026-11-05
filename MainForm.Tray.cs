using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 托盘职责：NotifyIcon 构建、菜单刷新、开合切换。
    /// 开机自启以 /startup 驻留托盘时，双击托盘正确"打开"而非"再最小化"。
    /// </summary>
    internal partial class MainForm
    {
        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = _appIcon, Text = $"暗幕 v{AppVersion}", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            _menu.Items.Add(GetStatusText()).Enabled = false;
            _menu.Items.Add("-");
            _menu.Items.Add(IsWindowOpen() ? "最小化到托盘" : "打开设置窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => _chkEnabled.Checked = !_chkEnabled.Checked);
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (_, _) => { _reallyQuit = true; Close(); });
        }

        /// <summary>窗口是否真正"打开"（可见且非最小化）。用于托盘菜单文案与开合判断，
        /// 这样开机自启以最小化态驻留时，双击托盘会正确地"打开"而非"再最小化"。</summary>
        private bool IsWindowOpen() => Visible && WindowState != FormWindowState.Minimized;

        private void ToggleWindow()
        {
            if (IsWindowOpen())
            {
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                ShowInTaskbar = true;
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
            }
        }
    }
}
