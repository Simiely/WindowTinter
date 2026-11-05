using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 领域逻辑层：目标窗口生命周期（绑定/释放/删除/全量解绑）与效果应用（透明度 + 黑底）。
    /// 这是"目标从配置到黑底生效"的主链路，问题优先在此定位。
    /// 状态由 Program.cs 的 _entries/_settings 持有；UI 重建由 MainForm.UI.cs 的 RebuildTargetList 承担；
    /// 外部事件桥接由 MainForm.Events.cs 承担。
    /// </summary>
    internal partial class MainForm
    {
        /// <summary>创建条目并挂载 OnUpdate——所有蒙版显示逻辑的唯一入口。</summary>
        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var plate = new BlackPlate();
            var entry = new TargetEntry { Info = info, Tracker = tracker, Plate = plate };
            plate.CornerRadius = _settings.GlobalCornerRadius ? _settings.CornerRadius : info.CornerRadius;
            tracker.OnUpdate += (r, visible) => ApplyEntryEffect(entry, visible);
            return entry;
        }

        /// <summary>
        /// 效果应用（唯一入口）：差异计算 alpha/黑底，有变化才落系统调用。
        /// 取代原先写在闭包里的逻辑，状态由 entry 显式持有，可测试、可复用。
        /// </summary>
        private void ApplyEntryEffect(TargetEntry entry, bool visible)
        {
            IntPtr h = entry.Tracker.TargetHandle;
            if (h == IntPtr.Zero) return;

            // 全局模式用全局透明度，否则用该目标自己的配置
            int bgPct = _settings.GlobalTransparency ? _settings.BackgroundAlpha : entry.Info.BackgroundAlpha;

            if (!_settings.Enabled || !visible)
            {
                entry.Plate.HidePlate();
                SetTargetTopmost(h, false); // 清理可能的历史置顶残留
                if (entry.LastAlpha != 255)
                {
                    SetTargetAlpha(h, 255, entry.OriginallyLayered ?? false);
                    entry.LastAlpha = 255;
                }
                return;
            }

            // 目标设半透明（前后台统一），正后方按需钉纯黑底板。
            // 注意：不置顶——窗口前后遮挡遵循 Windows 默认逻辑，仅"选中目标"时一次性带到前台（见 BringTargetToTop）。
            byte targetAlpha = (byte)((100 - bgPct) * 255 / 100);
            if (entry.LastAlpha != targetAlpha)
            {
                // 首次置透明前记录窗口原生 layered 状态，供恢复时判断能否安全移除该样式
                if (targetAlpha < 255 && entry.OriginallyLayered == null)
                {
                    long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    entry.OriginallyLayered = (ex & Native.WS_EX_LAYERED) != 0;
                }
                SetTargetAlpha(h, targetAlpha, entry.OriginallyLayered ?? false);
                entry.LastAlpha = targetAlpha;
            }
            if (_settings.BackdropBlackPlate)
            {
                // 用 DWM 扩展框架边界（去阴影）对齐底板，避免微信等程序遮罩外溢
                var visibleRect = Native.GetVisibleWindowRect(h);
                entry.Plate.AlignBehind(h, visibleRect);
            }
            else entry.Plate.HidePlate();
        }

        /// <summary>
        /// 取消目标窗口的置顶残留（TOPMOST 还原为普通 Z 序）。幂等。
        /// 仅用于清理历史版本可能留下的置顶样式，正常流程不置顶任何窗口——
        /// 窗口之间的前后遮挡关系完全遵循 Windows 默认逻辑。
        /// </summary>
        private static void SetTargetTopmost(IntPtr hwnd, bool topmost)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                bool isTop = (ex & Native.WS_EX_TOPMOST) != 0;
                if (isTop == topmost) return;

                Native.SetWindowPos(hwnd, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            catch (Exception ex2) { Debug.WriteLine($"SetTargetTopmost failed for 0x{hwnd:X}: {ex2.Message}"); }
        }

        /// <summary>
        /// 把目标窗口一次性带到 Z 序顶部（不激活、不抢焦点）。
        /// 仅"选中/指定目标"的那一刻调用一次，之后前后遮挡交给 Windows 默认逻辑——
        /// 其它窗口可以正常盖上来，目标不会霸占最前。
        /// </summary>
        private static void BringTargetToTop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Debug.WriteLine($"BringTargetToTop failed for 0x{hwnd:X}: {ex.Message}"); }
        }

        /// <summary>
        /// 设置目标窗口整体透明度。
        /// <paramref name="originallyLayered"/>：目标窗口"原生"（首次置透明前）是否已是 WS_EX_LAYERED。
        /// - alpha&lt;255：加 WS_EX_LAYERED + LWA_ALPHA；
        /// - alpha=255 且窗口原本就是分层窗口（浏览器等原生合成）：不粗暴移除样式，只把 alpha 设回 255，
        ///   避免破坏其原有合成状态（此前"透明度没反应/异常色块"的一类根因）；
        /// - alpha=255 且样式是我们加的：移除样式并 RedrawWindow 还原。
        /// </summary>
        private static void SetTargetAlpha(IntPtr hwnd, byte alpha, bool originallyLayered = false)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                bool hasLayered = (ex & Native.WS_EX_LAYERED) != 0;

                if (alpha >= 255)
                {
                    if (hasLayered && !originallyLayered)
                    {
                        // 样式是本程序加的 → 移除并重绘，彻底还原
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED));
                        // MSDN: 移除 WS_EX_LAYERED 后用 RedrawWindow 而非 InvalidateRect
                        Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                    }
                    else if (hasLayered)
                    {
                        // 原生分层窗口：保留样式，仅恢复全可见，避免破坏其合成
                        Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
                        Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                    }
                }
                else
                {
                    if (!hasLayered)
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_LAYERED));
                    Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
                    Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                }
            }
            catch (Exception ex2) { Debug.WriteLine($"SetTargetAlpha failed for 0x{hwnd:X}: {ex2.Message}"); }
        }

        // ── 提权提示：目标窗口以管理员运行、本程序非提权时无法修改其透明度（检测逻辑见 Elevation.cs）──

        private bool _elevationWarned;

        /// <summary>触发刷新——走 OnUpdate 完整路径（透明度 + 下方垫黑）。</summary>
        private void ApplyMaskNow(TargetEntry e)
        {
            e.Tracker.RefreshForeground();
        }

        /// <summary>
        /// 统一绑定入口：查找目标窗口 → 创建条目 → 入列 → 激活 → 挂 UI。返回是否绑定成功。
        /// refreshUI=false 供批量场景（重新查找/启动/定时器）使用，由调用方统一重建 UI。
        /// </summary>
        private bool BindTarget(TargetInfo info, bool refreshUI = true)
        {
            if (_entries.Any(e => e.Info == info)) return true; // 已绑定

            var boundHandles = new HashSet<IntPtr>(_entries.Select(e => e.Tracker.TargetHandle));
            var (h, matchKind) = TargetTracker.FindMatch(info.WindowTitle, info.ProcessName, boundHandles, info.WindowClass);
            if (h == IntPtr.Zero) return false;
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return false; // 该窗口已被其它目标占用

            Debug.WriteLine($"BindTarget: [{info}] -> 0x{h:X} (依据:{matchKind})");

            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            _entries.Add(entry);          // 先入列，状态一致后再激活
            entry.Tracker.RefreshNow();   // 触发 OnUpdate → ApplyEntryEffect → 黑底显示
            RefreshTargetSnapshot(info);  // 绑定成功立即抓一次窗口快照

            if (refreshUI)
            {
                SyncUI();
            }

            // 提权提示：目标以管理员身份运行而本程序未提权时，改透明度会静默失败
            if (!_elevationWarned && !Elevation.IsCurrentProcessElevated())
            {
                bool? elevated = Elevation.IsTargetElevated(h);
                if (elevated == true)
                {
                    _elevationWarned = true;
                    _tray?.ShowBalloonTip(5000, "无法修改此窗口透明度",
                        "目标窗口以管理员身份运行，而本程序不是。请右键“以管理员身份运行”本程序。",
                        ToolTipIcon.Warning);
                }
            }
            return true;
        }

        /// <summary>
        /// 统一释放入口：还原效果 → 释放资源 → 同步 UI（由 RebuildTargetList 按状态自动呈现"待激活"）。
        /// 生命周期内唯一允许"从监控中离开"的路径；窗口销毁 / 删除 / 暂停 / 解绑 / 退出全部走这里。
        /// </summary>
        private void ReleaseTarget(TargetEntry entry, string reason, bool updateUI = true)
        {
            Debug.WriteLine($"ReleaseTarget: [{entry.Info}] ({reason})");

            // 1) 还原目标窗口效果（逐项 try，单条失败不阻塞后续）
            try
            {
                if (entry.Tracker.TargetHandle != IntPtr.Zero && Native.IsWindow(entry.Tracker.TargetHandle))
                {
                    SetTargetAlpha(entry.Tracker.TargetHandle, 255, entry.OriginallyLayered ?? false);
                    SetTargetTopmost(entry.Tracker.TargetHandle, false);
                }
            }
            catch { }
            try { entry.Plate.HidePlate(); } catch { }

            // 2) 释放资源
            try { entry.Tracker.Dispose(); } catch { }
            try { entry.Plate.Dispose(); } catch { }

            _entries.Remove(entry);

            // 3) 同步 UI（窗体关闭等场景可跳过）
            if (updateUI)
                SyncUI();
        }

        /// <summary>统一删除入口：从配置移除 + 解绑（若有）+ 同步 UI + 保存。</summary>
        private void RemoveTarget(TargetInfo info)
        {
            _selectButtons.Remove(info);
            if (_selectedTarget != null && _selectedTarget.Equals(info)) _selectedTarget = null;

            _settings.Targets.Remove(info); // 先移出配置，后续 Rebuild 才不会残留该目标面板
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null) ReleaseTarget(entry, "removed");
            else SyncUI();
            _settings.Save();
        }

        /// <summary>全部解绑（保留配置，转待激活）。批量：先全部释放（不逐次重建 UI），最后统一重建一次。</summary>
        private void UnbindAll()
        {
            foreach (var e in _entries.ToList())
                ReleaseTarget(e, "unbind", updateUI: false);
            SyncUI();
        }

        /// <summary>
        /// UI 同步单点：重建目标列表 + 刷新状态栏/控件。
        /// 领域方法（Bind/Release/Remove/Unbind）只调这一个 UI 入口，不散落 Rebuild/Update 细节。
        /// </summary>
        private void SyncUI()
        {
            try { RebuildTargetList(); } catch { }
            UpdateUI();
        }
    }
}
