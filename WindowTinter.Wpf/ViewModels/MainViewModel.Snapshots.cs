using System;
using System.Linq;

namespace WindowTinter.ViewModels
{
    /// <summary>MainViewModel partial：卡片快照（后台抓图 + Freeze 跨线程）。</summary>
    internal partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // 快照
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 刷新单张卡片快照（绑定即时 / 移动即时 / 定时 / 手动全量）。
        /// 性能：抓图（PrintWindow GDI）+ 缩放 + 转 BitmapSource 全部放后台线程（Task.Run），
        /// UI 线程只收尾设置已 Freeze 的 BitmapSource——消除每 3s 全量刷新造成的界面卡顿。
        /// </summary>
        public void RefreshTargetSnapshot(TargetInfo info)
        {
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            var vm = FindVm(info);
            if (vm == null) return;
            if (entry == null || entry.Tracker.TargetHandle == IntPtr.Zero || !Native.IsWindow(entry.Tracker.TargetHandle))
            {
                vm.SetShotSource(null);
                return;
            }
            IntPtr h = entry.Tracker.TargetHandle;
            System.Threading.Tasks.Task.Run(() =>
            {
                var src = ImageConvert.ToBitmapSource(SnapshotService.Capture(h, 100, 48));
                if (src == null) return;
                var target = vm;
                _dispatcher.BeginInvoke(new Action(() => target?.SetShotSource(src)));
            });
        }

        /// <summary>全量刷新所有目标卡片快照（卡头「⟳ 刷新快照」/ 3s 定时）。</summary>
        public void RefreshAllSnapshots()
        {
            foreach (var t in _settings.Targets) RefreshTargetSnapshot(t);
        }

        private TargetViewModel FindVm(TargetInfo info)
            => _vmByInfo.TryGetValue(info, out var vm) ? vm : null;
    }
}
