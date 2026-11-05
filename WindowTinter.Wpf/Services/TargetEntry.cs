using System;

namespace WindowTinter
{
    /// <summary>目标条目：一个已绑定目标的运行时状态。封装：配置（TargetInfo）、跟踪器（TargetTracker）、黑底（BlackPlate）。</summary>
    internal class TargetEntry
    {
        public TargetInfo Info;
        public TargetTracker Tracker;
        public BlackPlate Plate;
        public byte LastAlpha = 255;          // 最近一次应用到目标窗口的 alpha（差异计算用）
        public bool? OriginallyLayered;       // 目标窗口原生是否 WS_EX_LAYERED（首次置透明前记录，用于还原时决定是否可移除该样式）

        // ── 绑定前捕获的原始状态（释放/退出时按原值还原，而不是写死 255 / NOTOPMOST）──
        public long OriginalExStyle;          // 绑定时的原始 GWL_EXSTYLE（含 LAYERED / TOPMOST 原值）
        public bool OriginalLayered;          // 原始是否分层窗口
        public bool OriginalTopmost;          // 原始是否置顶
        public bool OriginalAlphaKnown;       // 是否成功读回原始 alpha（UpdateLayeredWindow 窗口读不到）
        public byte OriginalAlpha = 255;      // 原始 alpha（SetLayeredWindowAttributes 读回值）
        public bool StateCaptured;            // 是否成功捕获（失败时释放回退到旧逻辑）
    }
}
