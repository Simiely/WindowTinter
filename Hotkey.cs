using System;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 全局热键辅助：常量与注册/注销包装。
    /// </summary>
    internal static class Hotkey
    {
        public const int Id = 1;

        public static bool Register(IntPtr hwnd, uint modifiers, uint vk) =>
            Native.RegisterHotKey(hwnd, Id, modifiers, vk);

        public static bool Unregister(IntPtr hwnd) =>
            Native.UnregisterHotKey(hwnd, Id);

        /// <summary>修饰符 + 虚拟键码 -> 可读字符串，仅用于托盘提示。</summary>
        public static string Describe(uint modifiers, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            string key = vk >= 0x41 && vk <= 0x5A
                ? ((char)vk).ToString()
                : Enum.GetName(typeof(Keys), (Keys)vk) ?? vk.ToString();
            parts.Add(key);
            return string.Join("+", parts);
        }
    }
}
