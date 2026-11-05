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
    }
}
