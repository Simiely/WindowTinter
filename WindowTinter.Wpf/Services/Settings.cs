using System;
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
        public int BackgroundAlpha { get; set; } = 50;  // 璇ョ洰鏍囩獥鍙ｉ€忔槑搴︼紙0~100锛夛紝浠?鍏ㄥ眬缁熶竴閫忔槑搴?鍏抽棴鏃剁敓鏁?
        public int CornerRadius { get; set; } = 15;       // 搴曟澘鍦嗚鍗婂緞 (0=鍏? 1-20px)锛屼粎"鍏ㄥ眬缁熶竴鍦嗚"鍏抽棴鏃剁敓鏁?

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
        public int BackgroundAlpha { get; set; } = 50;
        public bool Enabled { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool GlobalTransparency { get; set; } = true; // true=鎵€鏈夊簲鐢ㄧ粺涓€鐢ㄥ叏灞€閫忔槑搴︼紱false=姣忎釜鐩爣鍗曠嫭閰嶇疆
        public bool GlobalCornerRadius { get; set; } = true; // true=鎵€鏈夊簲鐢ㄧ粺涓€鐢ㄥ叏灞€鍦嗚锛沠alse=姣忎釜鐩爣鍗曠嫭閰嶇疆
        public bool BackdropBlackPlate { get; set; } = true;  // 鍦ㄧ洰鏍囨鍚庢柟鍙犲姞绾粦搴曟澘锛堜笅灞傞伄缃╋級锛岄粯璁ゅ紑鍚?
        public int CornerRadius { get; set; } = 15;            // 搴曟澘鍦嗚鍗婂緞 (0=鍏? 1-20px)锛岄粯璁?6px

        // 鏃у瓧娈碉紙浠呯敤浜庝粠 v2.x 鏃ф牸寮忚縼绉伙紝涓嶅啀鍐欏叆锛?
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetProcessName { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string TargetWindowTitle { get; set; } = "";
        // StartWithWindows 瀛楁宸插簾寮冿紝JSON 鍙嶅簭鍒楀寲鏃惰嚜鍔ㄥ拷鐣?

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

            // 杩佺Щ鏃?ProcessName 鍚庣紑锛氬幓鎺?.exe锛坴2.x 鏇剧粡瀛樺偍 "notepad.exe" 鏍煎紡锛?
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
                    // 甯?/startup 鍙傛暟锛氬紑鏈鸿嚜鍚椂鍙┗鐣欐墭鐩樸€佷笉寮逛富绐楀彛锛堟墜鍔ㄥ弻鍑?exe 涓嶅甫鍙傛暟鍒欐甯告樉绀猴級
                    key.SetValue("WindowTinter", $"\"{Environment.ProcessPath}\" /startup");
                else
                    key.DeleteValue("WindowTinter", false);
            }
            catch { }
        }
    }
}

