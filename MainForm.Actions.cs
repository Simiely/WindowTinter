using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// UI 动作层：按钮/滑块/复选框的回调入口。
    /// 只做「读控件 → 更新设置 → 触发领域逻辑（BindTarget/ApplyMaskNow/ApplyEntryEffect）」的编排，
    /// 不包含领域实现。
    /// </summary>
    internal partial class MainForm
    {
        private void ToggleEnabled()
        {
            // 直接读 checkbox 状态，避免 _settings.Enabled 翻转与 UI 不一致
            bool enable = _chkEnabled.Checked;
            _settings.Enabled = enable;
            _settings.Save();
            if (enable)
            {
                // 批量启用：全部重绑（不逐次重建 UI），最后统一 Rebuild 一次
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    BindTarget(t, refreshUI: false);
                }
                RebuildTargetList();
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            else
            {
                // 停用：统一走 ApplyEntryEffect（_settings.Enabled=false 时其内部走"还原 alpha + 隐藏黑底 + 清置顶"分支）
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            UpdateUI();
        }

        private void ToggleGlobalTransparency()
        {
            bool g = _chkGlobalTransparency.Checked;
            _settings.GlobalTransparency = g;
            if (!g)
            {
                // 关闭全局：把当前全局值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.BackgroundAlpha = _settings.BackgroundAlpha;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            foreach (var e in _entries) ApplyMaskNow(e);
        }

        private void ToggleGlobalCornerRadius()
        {
            bool g = _chkGlobalCornerRadius.Checked;
            _settings.GlobalCornerRadius = g;
            if (!g)
            {
                // 关闭全局：把当前全局圆角值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.CornerRadius = _settings.CornerRadius;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            // 刷新所有底板的圆角
            foreach (var e in _entries)
            {
                bool useGlobal = _settings.GlobalCornerRadius;
                e.Plate.CornerRadius = useGlobal ? _settings.CornerRadius : e.Info.CornerRadius;
                e.Tracker.RefreshForeground();
            }
        }

        private void SetBgAlpha(int value)
        {
            value = Math.Clamp(value, 0, 100);
            if (_settings.GlobalTransparency)
                _settings.BackgroundAlpha = value;
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.BackgroundAlpha = value;
            }
            foreach (var e in _entries) ApplyMaskNow(e);
            _lblBgAlpha.Text = $"{value}%";
            // 200ms 防抖：停止拖动后再写盘
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void SetCornerRadius(int value)
        {
            value = Math.Clamp(value, 0, 20);
            if (_settings.GlobalCornerRadius)
            {
                _settings.CornerRadius = value;
                foreach (var e in _entries)
                    SetPlateCornerRadius(e, value);
            }
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.CornerRadius = value;
                foreach (var e in _entries)
                {
                    if (e.Info == _selectedTarget)
                    {
                        SetPlateCornerRadius(e, value);
                        break;
                    }
                }
            }
            _lblCornerRadius.Text = value == 0 ? "关" : $"{value}px";
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private static void SetPlateCornerRadius(TargetEntry e, int value)
        {
            e.Plate.CornerRadius = value;
            e.Tracker.RefreshForeground();
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            string procName = "";
            try { procName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName ?? ""; } catch { }

            string title = "";
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                title = sb.ToString();
            }
            var info = new TargetInfo
            {
                ProcessName = procName,
                WindowTitle = title,
                // 记录窗口类名：重开后标题变化时仍可按 进程+类名 找回（见 TargetTracker.FindByTitleAndProcess）
                WindowClass = TargetTracker.GetWindowClass(picker.SelectedHandle)
            };

            // 以当前全局值作为该窗口独立配置的起点
            info.BackgroundAlpha = _settings.BackgroundAlpha;
            info.CornerRadius = _settings.CornerRadius;

            if (_settings.Targets.Contains(info))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);

            if (_settings.Enabled)
            {
                if (BindTarget(info))
                {
                    var entry = _entries.FirstOrDefault(e => e.Info == info);
                    if (entry != null)
                    {
                        BringTargetToTop(entry.Tracker.TargetHandle); // 新指定目标：一次性带到前台查看效果
                        ApplyMaskNow(entry);
                    }
                }
                else RebuildTargetList();
            }
            else RebuildTargetList();
            // 非全局模式下，自动选中刚添加的窗口
            if (!_settings.GlobalTransparency || !_settings.GlobalCornerRadius) _selectedTarget = info;
            UpdateUI();
        }

        private void RefindAllWindows()
        {
            // 重新查找 = 全量重绑定：先全部解绑（保留配置，转待激活），再按配置顺序重新绑定。
            // 批量模式：不逐次重建 UI，最后统一 Rebuild 一次呈现最终状态（活跃 + 待激活）。
            UnbindAll();
            foreach (var t in _settings.Targets)
                BindTarget(t, refreshUI: false);
            RebuildTargetList();
            UpdateUI();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string v = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.5.2";
            MessageBox.Show(
                $"暗幕 v{v}\n\n" +
                "给任意窗口设置透明度、并在其正下方垫纯黑的常驻小工具。\n" +
                "支持多窗口同时控制。\n\n" +
                "• 配置: 与 exe 同目录 WindowTinter.settings.json\n" +
                "• 图标: app.ico 与 exe 同目录\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }

        private void SaveSettings()
        {
            _settings.Save();
        }

        /// <summary>重命名目标窗口的显示名称（双击列表项或点 ✎ 触发）。输入空 = 清除别名恢复默认。</summary>
        private void RenameTarget(TargetInfo info)
        {
            using var dlg = new RenameDialog(info.Alias);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string alias = dlg.RenameText.Trim();
            info.Alias = alias; // 空字符串 = 清除别名，ToString 回退到 标题/进程名 兜底链
            _settings.Save();
            RebuildTargetList(); // 重建列表刷新显示名
            UpdateUI();
        }
    }
}
