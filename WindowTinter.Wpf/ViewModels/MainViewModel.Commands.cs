using System;
using System.IO;
using System.Linq;
using System.Windows;
using WindowTinter.Views;

namespace WindowTinter.ViewModels
{
    /// <summary>MainViewModel partial：命令（操作栏/卡头）与退出流程。</summary>
    internal partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // 命令（操作栏 / 卡头）
        // ════════════════════════════════════════════════════════════════

        public RelayCommand RefreshSnapshotsCommand { get; private set; }  // 卡头 ⟳
        public RelayCommand AddWindowCommand { get; private set; }         // + 添加窗口（D 阶段：WindowPickerWindow）
        public RelayCommand RefindAllCommand { get; private set; }         // ↻ 重新查找
        public RelayCommand SaveCommand { get; private set; }              // 💾 保存配置
        public RelayCommand ExitCommand { get; private set; }              // 🚪 退出
        public RelayCommand OpenConfigFolderCommand { get; private set; }  // 📂 配置文件夹
        public RelayCommand AboutCommand { get; private set; }             // ℹ 关于

        private void BuildCommands()
        {
            RefreshSnapshotsCommand = new RelayCommand(RefreshAllSnapshots);
            AddWindowCommand = new RelayCommand(PickWindow);
            RefindAllCommand = new RelayCommand(RefindAllWindows);
            SaveCommand = new RelayCommand(() => _settings.Save());
            ExitCommand = new RelayCommand(ExitApplication);
            OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
            AboutCommand = new RelayCommand(ShowAbout);
        }

        /// <summary>真实退出标记：MainWindow.Closing 据此走"释放+退出"而非"最小化到托盘"。</summary>
        public bool ReallyQuit { get; set; }

        /// <summary>托盘驻留时的保存（不释放效果）。</summary>
        public void SaveSettings() => _settings.Save();

        /// <summary>暴露 Settings 引用（用于窗口状态保存/恢复，View 直接读写字段）。</summary>
        public Settings GetSettings() => _settings;

        /// <summary>退出：置真实退出标记 → 关主窗（Closing 兜底释放全部）。</summary>
        public void ExitApplication()
        {
            ReallyQuit = true;
            var w = Application.Current.MainWindow;
            if (w != null) w.Close();
            else { Shutdown(); Application.Current.Shutdown(); }
        }

        /// <summary>窗体关闭兜底（MainWindow.Closing）：不托盘时直接退出；托盘逻辑 D 阶段接入。</summary>
        public void Shutdown()
        {
            _autoBindTimer?.Stop();
            _saveDebounceTimer?.Stop();
            if (_winEventHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            foreach (var e in _entries.ToList())
            {
                try { ReleaseTarget(e, "quit", updateUI: false); } catch { }
            }
            _settings.Save();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            var dlg = new AboutDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }
    }
}
