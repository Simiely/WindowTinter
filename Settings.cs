using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WindowTinter
{
    /// <summary>存储一个目标窗口的标识信息（hwnd 不持久化，按进程名+标题重新查找）。</summary>
    internal class TargetInfo : IEquatable<TargetInfo>
    {
        public string ProcessName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public int Alpha { get; set; } = 75;            // 该目标前台蒙版暗度（0~100），仅“全局统一透明度”关闭时生效
        public int BackgroundAlpha { get; set; } = 50;  // 该目标退到后台时的透明度（0~100），仅“全局统一透明度”关闭时生效

        public override string ToString() => string.IsNullOrEmpty(WindowTitle) ? ProcessName : WindowTitle;

        public bool Equals(TargetInfo other) =>
            other != null &&
            string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(WindowTitle, other.WindowTitle, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => Equals(obj as TargetInfo);

        public override int GetHashCode() =>
            HashCode.Combine(
                ProcessName?.ToLowerInvariant() ?? "",
                WindowTitle?.ToLowerInvariant() ?? "");

        public static bool operator ==(TargetInfo a, TargetInfo b) =>
            ReferenceEquals(a, b) || (a is not null && a.Equals(b));

        public static bool operator !=(TargetInfo a, TargetInfo b) => !(a == b);
    }

    /// <summary>
    /// 持久化设置。支持多窗口目标列表。
    /// 配置存储于 exe 同目录 WindowTinter.settings.json
    /// </summary>
    internal class Settings
    {
        public List<TargetInfo> Targets { get; set; } = new();
        public int Alpha { get; set; } = 75;
        public int BackgroundAlpha { get; set; } = 50;
        public bool Enabled { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool KeepTransparency { get; set; } = false;  // 开启后不用蒙版，窗口保持恒定透明度
        public bool GlobalTransparency { get; set; } = true; // true=所有应用统一用全局透明度；false=每个目标单独配置

        // 旧字段（仅用于从 v2.x 旧格式迁移，不再写入）
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetProcessName { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetWindowTitle { get; set; } = "";
        // StartWithWindows 字段已废弃，JSON 反序列化时自动忽略

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string ConfigDir =>
            Path.GetDirectoryName(Environment.ProcessPath) ?? ".";

        private static string FilePath => Path.Combine(ConfigDir, "WindowTinter.settings.json");

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), _jsonOptions);
            }
            catch { /* 损坏则回退默认 */ }

            // 尝试从旧位置迁移（exe 同目录的旧配置文件）
            if (s == null)
            {
                var oldPath = Path.Combine(
                    Path.GetDirectoryName(Environment.ProcessPath) ?? ".",
                    "WindowTinter.settings.json");
                try
                {
                    if (File.Exists(oldPath))
                    {
                        s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(oldPath), _jsonOptions);
                        if (s != null) s.Save(); // 迁移到新位置
                    }
                }
                catch { }
            }

            s ??= new Settings();

            // 迁移旧 Alpha 格式（0-255 → 0-100）
            if (s.Alpha > 100) s.Alpha = s.Alpha * 100 / 255;
            if (s.BackgroundAlpha > 100) s.BackgroundAlpha = s.BackgroundAlpha * 100 / 255;

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
            }

            // 迁移旧 ProcessName 后缀：去掉 .exe（v2.x 曾经存储 "notepad.exe" 格式）
            bool migratedExe = false;
            foreach (var t in s.Targets)
            {
                if (!string.IsNullOrEmpty(t.ProcessName) && t.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    t.ProcessName = t.ProcessName[..^4];
                    migratedExe = true;
                }
            }
            if (migratedExe) s.Save();

            return s;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Settings.Save failed: {ex.Message}"); }
        }

        public void ApplyStartWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (StartWithWindows)
                    key.SetValue("WindowTinter", Environment.ProcessPath ?? "");
                else
                    key.DeleteValue("WindowTinter", false);
            }
            catch { }
        }
    }
}
