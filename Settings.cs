using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WindowTinter
{
    internal class TargetInfo
    {
        public string ProcessName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public override string ToString() => string.IsNullOrEmpty(WindowTitle) ? ProcessName : WindowTitle;
    }

    internal class Settings
    {
        public List<TargetInfo> Targets { get; set; } = new();
        public int Alpha { get; set; } = 75;
        public bool Enabled { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool DebugEnabled { get; set; } = true;

        // 旧字段（v2.x 迁移用，不再写入）
        public string TargetProcessName { get; set; } = "";
        public string TargetWindowTitle { get; set; } = "";
        public string Mode { get; set; } = "Mask";

        private static string FilePath => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WindowTinter.settings.json");

        public static Settings Load()
        {
            Settings s = null;
            try { if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)); } catch { }
            s ??= new Settings();

            // 迁移旧 Alpha 格式（0-255 → 0-100）
            if (s.Alpha > 100) s.Alpha = s.Alpha * 100 / 255;

            // 迁移旧单窗口格式 → 列表
            if (s.Targets.Count == 0 && !string.IsNullOrEmpty(s.TargetProcessName))
            {
                s.Targets.Add(new TargetInfo { ProcessName = s.TargetProcessName, WindowTitle = s.TargetWindowTitle });
                s.TargetProcessName = s.TargetWindowTitle = "";
                s.Save();
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void ApplyStartWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (StartWithWindows) key.SetValue("WindowTinter", System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                else key.DeleteValue("WindowTinter", false);
            }
            catch { }
        }
    }
}
