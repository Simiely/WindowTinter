using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WindowTinter
{
    /// <summary>
    /// 持久化设置。目标窗口用"进程名"保存（hwnd 跨进程/重启不稳定），启动时按进程名重新查找。
    /// </summary>
    internal class Settings
    {
        public string TargetProcessName { get; set; } = "BaiduNetdisk.exe";
        public string Mode { get; set; } = "Mask"; // "Mask" 或 "Invert"
        public int Alpha { get; set; } = 115;       // 0-255，约 45% 不透明黑
        public uint HotkeyModifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;
        public uint HotkeyVk { get; set; } = 0x44;  // 'D'
        public bool Enabled { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowTinter", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch { /* 损坏则回退默认 */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
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
