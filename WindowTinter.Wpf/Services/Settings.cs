﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WindowTinter
{
    /// <summary>瀛樺偍涓€涓洰鏍囩獥鍙ｇ殑鏍囪瘑淇℃伅锛坔wnd 涓嶆寔涔呭寲锛屾寜杩涚▼鍚?鏍囬/绫诲悕閲嶆柊鏌ユ壘锛夈€?/summary>
    internal class TargetInfo : IEquatable<TargetInfo>
    {
        public string ProcessName { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        /// <summary>绐楀彛绫诲悕锛堟嬀鍙栨椂璁板綍锛夈€傞噸缁戝畾涓婚敭涔嬩竴锛氭祻瑙堝櫒/缂栬緫鍣ㄦ爣棰樺彉鍖栨椂鎸?杩涚▼+绫诲悕 浠嶈兘鎵惧洖銆?/summary>
        public string WindowClass { get; init; } = "";
        /// <summary>鑷畾涔夊埆鍚嶏紙鐢ㄦ埛閲嶅懡鍚嶏級銆傞潪绌烘椂 UI 鍒楄〃浼樺厛鏄剧ず瀹冦€侸SON 缂虹渷涓虹┖锛屾棫閰嶇疆鍚戝悗鍏煎銆?/summary>
        public string Alias { get; set; } = "";
        /// <summary>单卡启用开关。与全局 Enabled 分层：全局开关是总闸，单卡只决定这张卡片是否遮罩该窗口。
        /// 旧配置没有该字段；System.Text.Json 反序列化会先执行属性初始化器，缺失字段保持 true（启用）。</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>单卡已自定义（压暗强度/圆角）：true 后不再跟随全局默认值。
        /// 旧配置无此字段 → 反序列化保留初始化值 false，即全部跟随全局，不会把全局值硬拷贝成自定义值。</summary>
        public bool IsCustomized { get; set; } = false;
        public int BackgroundAlpha { get; set; } = 50;   // 单卡自定义压暗强度（0~100）；IsCustomized=false 时忽略，取全局值
        public int CornerRadius { get; set; } = 14;       // 单卡自定义圆角（0~32px）；IsCustomized=false 时忽略，取全局值

        /// <summary>鏄剧ず鍚嶏細鍒悕锛堢敤鎴烽噸鍛藉悕锛変紭鍏堬紝鍏舵绐楀彛鏍囬锛屽啀閫€鍥炶繘绋嬪悕銆傚悕绉板厹搴曚氦缁欑敤鎴烽噸鍛藉悕锛屼笉鐗规畩澶勭悊鏃犲悕绉扮獥鍙ｃ€?/summary>
        public override string ToString() =>
            !string.IsNullOrEmpty(Alias) ? Alias
            : !string.IsNullOrEmpty(WindowTitle) ? WindowTitle
            : ProcessName;

        public bool Equals(TargetInfo other) =>
            other != null &&
            string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(WindowTitle, other.WindowTitle, StringComparison.OrdinalIgnoreCase) &&
            // 绐楀彛绫诲悕浠呭湪鍙屾柟閮介潪绌烘椂鍙備笌鍒ゅ畾锛堟棫閰嶇疆 WindowClass="" 鏃惰韩浠介€€鍖栦负 杩涚▼+鏍囬锛屽悜鍚庡吋瀹癸級
            (string.IsNullOrEmpty(WindowClass) || string.IsNullOrEmpty(other.WindowClass)
                || string.Equals(WindowClass, other.WindowClass, StringComparison.OrdinalIgnoreCase));

        public override bool Equals(object obj) => Equals(obj as TargetInfo);

        // 娉ㄦ剰锛欸etHashCode 鏁呮剰鍙熀浜?杩涚▼+鏍囬鈥斺€?
        // Equals 鍏佽"涓€鏂?class 涓虹┖鍒欎笉姣旇緝 class"锛岃嫢鎶?class 璁″叆鍝堝笇浼氳繚鍙?
        // "Equals 鐩哥瓑鍒欏搱甯屽繀鐩哥瓑"鐨勭害鏉燂紙鏃ф潯鐩?class 绌?vs 鏂版潯鐩?class 闈炵┖浼氬垽绛夛級銆?
        public override int GetHashCode() =>
            HashCode.Combine(
                ProcessName?.ToLowerInvariant() ?? "",
                WindowTitle?.ToLowerInvariant() ?? "");

        public static bool operator ==(TargetInfo a, TargetInfo b) =>
            ReferenceEquals(a, b) || (a is not null && a.Equals(b));

        public static bool operator !=(TargetInfo a, TargetInfo b) => !(a == b);
    }

    /// <summary>
    /// 鎸佷箙鍖栬缃€傛敮鎸佸绐楀彛鐩爣鍒楄〃銆?
    /// 閰嶇疆瀛樺偍浜?exe 鍚岀洰褰?WindowTinter.settings.json
    /// </summary>
    internal class Settings
    {
        public List<TargetInfo> Targets { get; set; } = new();
        public int BackgroundAlpha { get; set; } = 50;   // 全局默认压暗强度（0~100）；未自定义的卡片跟随此值
        public int CornerRadius { get; set; } = 14;       // 全局默认圆角（0~32px）；未自定义的卡片跟随此值
        public bool Enabled { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool BackdropBlackPlate { get; set; } = true;
        // 窗口状态（关闭时保存最终位置/尺寸/最大化，下次启动恢复）
        public double WindowLeft { get; set; } = -1;
        public double WindowTop { get; set; } = -1;
        public double WindowWidth { get; set; } = 0;
        public double WindowHeight { get; set; } = 0;
        public bool WindowMaximized { get; set; } = false;
        // 每行卡片数（1 / 2）。旧配置无此字段 → 反序列化保留初始化值 1。
        public int CardsPerRow { get; set; } = 1;
        // 底部「全局默认」折叠面板的展开状态。旧配置无此字段 → 默认折叠。
        public bool GlobalPanelExpanded { get; set; } = false;
        // 旧字段：仅用于旧格式迁移读取，不再写入
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetProcessName { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetWindowTitle { get; set; } = "";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // Single-file app: settings live next to the exe (as before 015c44a).
        private static string FilePath =>
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "WindowTinter.settings.json");

        // One-shot reverse migration for the build that briefly used %LOCALAPPDATA%.
        // Copies the file back only when no exe-dir file exists, then deletes that
        // directory, so the branch can never fire twice.
        private static string LegacyDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowTinter");

        private static string LegacyFile => Path.Combine(LegacyDir, "WindowTinter.settings.json");

        public static Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath) && File.Exists(LegacyFile))
                {
                    File.Copy(LegacyFile, FilePath, overwrite: false);
                    if (new FileInfo(FilePath).Length == new FileInfo(LegacyFile).Length)
                        try { Directory.Delete(LegacyDir, recursive: true); } catch { }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Settings reverse migration failed: {ex.Message}"); }

            Settings s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), _jsonOptions);
            }
            catch { /* 鎹熷潖鍒欏洖閫€榛樿 */ }

            s ??= new Settings();

            // 杩佺Щ鏃ч€忔槑搴︽牸寮忥紙0-255 鈫?0-100锛?
            if (s.BackgroundAlpha > 100) s.BackgroundAlpha = s.BackgroundAlpha * 100 / 255;

            // 杩佺Щ鏃ф牸寮忥細鍗曠獥鍙?鈫?鍒楄〃
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

            // 旧格式兼容：老版本用 GlobalTransparency / GlobalCornerRadius 两个开关控制
            // 「全局统一」还是「按目标值生效」。升级后语义收敛为「每卡是否已自定义」：
            // 旧开关为 false（当时按单卡值生效）→ 该卡标记为已自定义，保留其单卡值；
            // 旧开关为 true 的维度写回当前全局值，绝不把全局值硬拷贝成该卡的自定义值。
            // 字段缺失 = 旧默认 true = 全部未自定义、全部跟随全局。
            bool legacyAlphaUnified = true, legacyCornerUnified = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                    legacyAlphaUnified = LegacyUnifiedFlag(doc.RootElement, "GlobalTransparency");
                    legacyCornerUnified = LegacyUnifiedFlag(doc.RootElement, "GlobalCornerRadius");
                }
            }
            catch { }
            if (!legacyAlphaUnified || !legacyCornerUnified)
            {
                foreach (var t in s.Targets)
                {
                    t.IsCustomized = true;
                    if (legacyAlphaUnified) t.BackgroundAlpha = s.BackgroundAlpha;
                    if (legacyCornerUnified) t.CornerRadius = s.CornerRadius;
                }
            }

            // 杩佺Щ鏃?ProcessName 鍚庣紑锛氬幓鎺?.exe锛坴2.x 鏇剧粡瀛樺偍 "notepad.exe" 鏍煎紡锛?
            bool migratedExe = false;
            for (int i = 0; i < s.Targets.Count; i++)
            {
                var t = s.Targets[i];
                if (!string.IsNullOrEmpty(t.ProcessName) && t.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep Alias/WindowClass/alpha/radius when trimming the legacy ".exe" suffix.
                    s.Targets[i] = new TargetInfo
                    {
                        ProcessName = t.ProcessName[..^4],
                        WindowTitle = t.WindowTitle,
                        WindowClass = t.WindowClass,
                        Alias = t.Alias,
                        BackgroundAlpha = t.BackgroundAlpha,
                        CornerRadius = t.CornerRadius,
                        Enabled = t.Enabled,
                        IsCustomized = t.IsCustomized
                    };
                    migratedExe = true;
                }
            }
            if (migratedExe) s.Save();

            // 迁移/兜底：每行卡片数只接受 1 或 2（旧配置缺失时由初始化器给 1）
            if (s.CardsPerRow < 1 || s.CardsPerRow > 2) s.CardsPerRow = 1;

            return s;
        }

        /// <summary>读取旧 JSON 的「全局统一」开关：字段缺失按 true（旧默认）；只有明确的 false 才表示按单卡值生效。</summary>
        private static bool LegacyUnifiedFlag(JsonElement root, string property)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var v)) return true;
            return v.ValueKind != JsonValueKind.False;
        }

        public void Save()
        {
            try
            {
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
                    // 甯?/startup 鍙傛暟锛氬紑鏈鸿嚜鍚椂鍙┗鐣欐墭鐩樸€佷笉寮逛富绐楀彛锛堟墜鍔ㄥ弻鍑?exe 涓嶅甫鍙傛暟鍒欐甯告樉绀猴級
                    key.SetValue("WindowTinter", $"\"{Environment.ProcessPath}\" /startup");
                else
                    key.DeleteValue("WindowTinter", false);
            }
            catch { }
        }
    }
}

