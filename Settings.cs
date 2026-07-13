using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WindowTinter
{
    /// <summary>存储一个目标窗口的标识信息（hwnd 不持久化，按进程名+标题重新查找）。</summary>
    internal class TargetInfo
    {
        public string ProcessName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public override string ToString() => string.IsNullOrEmpty(WindowTitle) ? ProcessName : WindowTitle;
    }

    /// <summary>
    /// 持久化设置。支持多窗口目标列表。
    /// </summary>
    internal class Settings
    {
        public List<TargetInfo> Targets { get; set; } = new();
        public string Mode { get; set; } = "Mask";
        public int Alpha { get; set; } = 75;
        public bool Enabled { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool DebugEnabled { get; set; } = true;

        // 旧字段（仅用于从 v2.x 旧格式迁移，不再写入）
        public string TargetProcessName { get; set; } = "";
        public string TargetWindowTitle { get; set; } = "";

        private static string AppDir =>
            Path.GetDirectoryName(Environment.ProcessPath);

        private static string FilePath => Path.Combine(AppDir, "WindowTinter.settings.json");

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            }
            catch { /* 损坏则回退默认 */ }

            s ??= new Settings();

            // 迁移旧格式：单窗口 → 列表
            if (s.Targets.Count == 0 && !string.IsNullOrEmpty(s.TargetProcessName))
            {
                s.Targets.Add(new TargetInfo
                {
                    ProcessName = s.TargetProcessName,
                    WindowTitle = s.TargetWindowTitle
                });
                s.TargetProcessName = "";
                s.TargetWindowTitle = "";
                s.Save();
            }

            return s;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppDir)) Directory.CreateDirectory(AppDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 忽略保存失败 */ }
        }

        public void ApplyStartWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (StartWithWindows)
                    key.SetValue("WindowTinter", System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                else
                    key.DeleteValue("WindowTinter", false);
            }
            catch { /* 忽略 */ }
        }
    }
}
