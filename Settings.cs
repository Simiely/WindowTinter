using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WindowTinter
{
    /// <summary>存储一个目标窗口的标识信息（hwnd 不持久化，按进程名+标题/类名重新查找）。</summary>
    internal class TargetInfo : IEquatable<TargetInfo>
    {
        public string ProcessName { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        /// <summary>窗口类名（拾取时记录）。重绑定主键之一：浏览器/编辑器标题变化时按 进程+类名 仍能找回。</summary>
        public string WindowClass { get; init; } = "";
        public int BackgroundAlpha { get; set; } = 50;  // 该目标窗口透明度（0~100），仅"全局统一透明度"关闭时生效
        public int CornerRadius { get; set; } = 15;       // 底板圆角半径 (0=关, 1-20px)，仅"全局统一圆角"关闭时生效

        public override string ToString() => string.IsNullOrEmpty(WindowTitle) ? ProcessName : WindowTitle;

        public bool Equals(TargetInfo other) =>
            other != null &&
            string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(WindowTitle, other.WindowTitle, StringComparison.OrdinalIgnoreCase) &&
            // 窗口类名仅在双方都非空时参与判定（旧配置 WindowClass="" 时身份退化为 进程+标题，向后兼容）
            (string.IsNullOrEmpty(WindowClass) || string.IsNullOrEmpty(other.WindowClass)
                || string.Equals(WindowClass, other.WindowClass, StringComparison.OrdinalIgnoreCase));

        public override bool Equals(object obj) => Equals(obj as TargetInfo);

        // 注意：GetHashCode 故意只基于 进程+标题——
        // Equals 允许"一方 class 为空则不比较 class"，若把 class 计入哈希会违反
        // "Equals 相等则哈希必相等"的约束（旧条目 class 空 vs 新条目 class 非空会判等）。
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
        public int BackgroundAlpha { get; set; } = 50;
        public bool Enabled { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool GlobalTransparency { get; set; } = true; // true=所有应用统一用全局透明度；false=每个目标单独配置
        public bool GlobalCornerRadius { get; set; } = true; // true=所有应用统一用全局圆角；false=每个目标单独配置
        public bool BackdropBlackPlate { get; set; } = true;  // 在目标正后方叠加纯黑底板（下层遮罩），默认开启
        public int CornerRadius { get; set; } = 15;            // 底板圆角半径 (0=关, 1-20px)，默认 6px

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

            s ??= new Settings();

            // 迁移旧透明度格式（0-255 → 0-100）
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
            for (int i = 0; i < s.Targets.Count; i++)
            {
                var t = s.Targets[i];
                if (!string.IsNullOrEmpty(t.ProcessName) && t.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    s.Targets[i] = new TargetInfo { ProcessName = t.ProcessName[..^4], WindowTitle = t.WindowTitle };
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
                    // 带 /startup 参数：开机自启时只驻留托盘、不弹主窗口（手动双击 exe 不带参数则正常显示）
                    key.SetValue("WindowTinter", $"\"{Environment.ProcessPath}\" /startup");
                else
                    key.DeleteValue("WindowTinter", false);
            }
            catch { }
        }
    }
}
