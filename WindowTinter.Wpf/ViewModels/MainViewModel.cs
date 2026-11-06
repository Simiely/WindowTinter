using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowTinter.Views;

namespace WindowTinter.ViewModels
{
    /// <summary>
    /// 主 VM：目标生命周期（绑定/释放/删除）+ 效果应用（透明度+黑底）+ UI 状态。
    /// 铁律：领域逻辑（BindTarget/ReleaseTarget/ApplyEntryEffect/SetTargetAlpha/SetTargetTopmost/
    /// BringTargetToTop/RemoveTarget）原样搬自 WinForms 版 MainForm，不改逻辑；
    /// 仅容器适配：Timer→DispatcherTimer、BeginInvoke→Dispatcher.BeginInvoke、托盘气泡→MessageBox（D 阶段换回托盘）。
    /// </summary>
    internal class MainViewModel : ObservableObject
    {
        private readonly Settings _settings = Settings.Load();
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, TargetViewModel> _vmByInfo = new();

        public ObservableCollection<TargetViewModel> Targets { get; } = new();

        // ── 列表视图（纯表现层：筛选 + 搜索，不改变目标集合本身）──
        public ICollectionView TargetsView { get; }
        private string _targetFilter = "all";       // all / active / pending
        private string _searchText = "";

        private TargetViewModel _selectedTarget;
        public TargetViewModel SelectedTarget
        {
            get => _selectedTarget;
            private set
            {
                if (ReferenceEquals(_selectedTarget, value)) return;
                _selectedTarget = value;
                foreach (var vm in _vmByInfo.Values) vm.RefreshCardVisuals();
                RaiseSliderProperties();
            }
        }

        // ── 定时器（WPF DispatcherTimer，UI 线程）──
        private readonly DispatcherTimer _autoBindTimer;    // 3s 兜底重绑
        private readonly DispatcherTimer _saveDebounceTimer; // 200ms 滑块防抖写盘

        // UI 线程 Dispatcher（构造时捕获；后台快照线程回投用）
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        // ── WinEvent 钩子（前台切换/移动/销毁即时响应）──
        private Native.WinEventProc _winEventProc;
        private IntPtr _winEventHook = IntPtr.Zero;

        // 已绑定目标句柄快照：WinEvent 回调线程只读，用于过滤无关窗口事件（不把全桌面事件投递到 UI 线程）
        private volatile IntPtr[] _boundHandles = Array.Empty<IntPtr>();
        // 正在抓图的目标句柄：同一目标已有 PrintWindow 在跑时不重复排队
        private readonly HashSet<IntPtr> _snapshotInFlight = new HashSet<IntPtr>();

        private bool _elevationWarned;

        public MainViewModel()
        {
            TargetsView = CollectionViewSource.GetDefaultView(Targets);
            TargetsView.Filter = FilterTarget;

            BuildCommands();
            _settings.ApplyStartWithWindows();

            // 先建卡片 VM（占位态），再绑定——绑定成功时快照能立即落到卡片上
            RebuildTargetList();

            // 启动绑定：按配置顺序全部尝试（批量，不逐次重建 UI）
            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    BindTarget(t, refreshUI: false);
                }
            }

            // 3s 自动重绑（窗口销毁→待激活→重开自动绑回）
            _autoBindTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;
                bool anyBound = false;
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    if (BindTarget(t, refreshUI: false)) anyBound = true;
                }
                if (anyBound) SyncUI();
            };
            _autoBindTimer.Start();

            _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _saveDebounceTimer.Tick += (_, _) => { _saveDebounceTimer.Stop(); _settings.Save(); };

            // 快照改为手动模式：仅「⟳ 刷新快照」按钮触发，不做 3s 定时自动刷新

            InstallWinEventHook();

            SyncUI();
        }

        // ════════════════════════════════════════════════════════════════
        // 领域逻辑：目标生命周期（原样搬自 MainForm.cs / MainForm.Actions.cs）
        // ════════════════════════════════════════════════════════════════

        /// <summary>创建条目并挂载 OnUpdate——所有蒙版显示逻辑的唯一入口。</summary>
        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var plate = new BlackPlate();
            var entry = new TargetEntry { Info = info, Tracker = tracker, Plate = plate };
            plate.CornerRadius = EffectiveCorner(info);
            tracker.OnUpdate += (r, visible) => ApplyEntryEffect(entry, visible);
            return entry;
        }

        /// <summary>效果应用（唯一入口）：任何 Win32 失败都记录后继续，不得冒到 UI 线程。</summary>
        private void ApplyEntryEffect(TargetEntry entry, bool visible)
        {
            try { ApplyEntryEffectCore(entry, visible); }
            catch (Exception ex) { Debug.WriteLine($"ApplyEntryEffect failed for 0x{entry.Tracker.TargetHandle:X}: {ex.Message}"); }
        }

        /// <summary>效果应用实现：差异计算 alpha/黑底，有变化才落系统调用。</summary>
        private void ApplyEntryEffectCore(TargetEntry entry, bool visible)
        {
            IntPtr h = entry.Tracker.TargetHandle;
            if (h == IntPtr.Zero) return;

            int bgPct = EffectiveAlpha(entry.Info);

            // 最小化抑制兜底：恢复后（可见且非图标化）且已过动画窗口，才允许底板重新显示
            if (entry.MinimizeSuppressed && visible &&
                Environment.TickCount64 - entry.MinimizeSuppressedAtMs >= 250)
                entry.MinimizeSuppressed = false;

            // 全局总闸关闭 / 本卡停用 / 目标不可见 / 最小化抑制：一律隐藏底板并还原窗口原透明度。
            // 单卡停用与全局暂停分层：全局关闭时各卡自己的 Enabled 状态保持不变。
            if (!_settings.Enabled || !entry.Info.Enabled || !visible || entry.MinimizeSuppressed)
            {
                entry.Plate.HidePlate();
                // 目标本来就置顶时不剥掉；否则清理历史置顶残留
                SetTargetTopmost(h, entry.StateCaptured && entry.OriginalTopmost);
                // 还原到绑定前的 alpha（原本就分层的目标可能是 200 之类的自定义值，不能写死 255）
                byte restoreAlpha = entry.StateCaptured && entry.OriginalLayered ? entry.OriginalAlpha : (byte)255;
                if (entry.LastAlpha != restoreAlpha)
                {
                    SetTargetAlpha(h, restoreAlpha, entry.OriginallyLayered ?? false);
                    entry.LastAlpha = restoreAlpha;
                }
                return;
            }

            byte targetAlpha = (byte)((100 - bgPct) * 255 / 100);
            if (entry.LastAlpha != targetAlpha)
            {
                if (targetAlpha < 255 && entry.OriginallyLayered == null)
                {
                    long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    entry.OriginallyLayered = (ex & Native.WS_EX_LAYERED) != 0;
                }
                SetTargetAlpha(h, targetAlpha, entry.OriginallyLayered ?? false);
                entry.LastAlpha = targetAlpha;
            }
            // cloak 时目标不在当前桌面/已挂起：隐藏底板，否则会留下黑块（DWM 不会发事件）
            if (_settings.BackdropBlackPlate && !entry.Tracker.IsTargetCloaked)
            {
                var visibleRect = Native.GetVisibleWindowRect(h);
                entry.Plate.AlignBehind(h, visibleRect);
            }
            else entry.Plate.HidePlate();
        }

        /// <summary>取消目标窗口的置顶残留（TOPMOST 还原为普通 Z 序）。幂等。</summary>
        private static void SetTargetTopmost(IntPtr hwnd, bool topmost)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                bool isTop = (ex & Native.WS_EX_TOPMOST) != 0;
                if (isTop == topmost) return;

                if (!Native.SetWindowPos(hwnd, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE))
                {
                    Debug.WriteLine($"SetTargetTopmost({topmost}) failed for 0x{hwnd:X}");
                }
            }
            catch (Exception ex2) { Debug.WriteLine($"SetTargetTopmost failed for 0x{hwnd:X}: {ex2.Message}"); }
        }

        /// <summary>把目标窗口一次性带到 Z 序顶部（不激活、不抢焦点）。仅选中/指定目标那一刻调用。</summary>
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

        /// <summary>设置目标窗口整体透明度（原样搬）。</summary>
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
                        if (Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED)) == IntPtr.Zero)
                            Debug.WriteLine($"SetWindowLongPtr(clear LAYERED) failed for 0x{hwnd:X}");
                        Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                    }
                    else if (hasLayered)
                    {
                        if (!Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA))
                            Debug.WriteLine($"SetLayeredWindowAttributes(255) failed for 0x{hwnd:X}");
                        Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                    }
                }
                else
                {
                    if (!hasLayered &&
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_LAYERED)) == IntPtr.Zero)
                        Debug.WriteLine($"SetWindowLongPtr(set LAYERED) failed for 0x{hwnd:X}");
                    if (!Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA))
                        Debug.WriteLine($"SetLayeredWindowAttributes({alpha}) failed for 0x{hwnd:X}");
                    Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                }
            }
            catch (Exception ex2) { Debug.WriteLine($"SetTargetAlpha failed for 0x{hwnd:X}: {ex2.Message}"); }
        }

        /// <summary>绑定前捕获原始 ExStyle / alpha / topmost。失败只标记 StateCaptured=false，释放时回退旧逻辑。</summary>
        private static void CaptureOriginalState(TargetEntry entry, IntPtr h)
        {
            try
            {
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                entry.OriginalExStyle = ex;
                entry.OriginalLayered = (ex & Native.WS_EX_LAYERED) != 0;
                entry.OriginalTopmost = (ex & Native.WS_EX_TOPMOST) != 0;
                entry.OriginallyLayered = entry.OriginalLayered; // 与既有 SetTargetAlpha 判定保持一致
                entry.OriginalAlphaKnown = false;
                entry.OriginalAlpha = 255;

                // 原生分层窗口：读回它自己的 alpha（UpdateLayeredWindow 窗口读不到，保持 Unknown）
                if (entry.OriginalLayered &&
                    Native.GetLayeredWindowAttributes(h, out _, out byte a, out uint flags) &&
                    (flags & Native.LWA_ALPHA) != 0)
                {
                    entry.OriginalAlpha = a;
                    entry.OriginalAlphaKnown = true;
                }
                entry.StateCaptured = true;
            }
            catch (Exception ex)
            {
                entry.StateCaptured = false;
                Debug.WriteLine($"CaptureOriginalState failed for 0x{h:X}: {ex.Message}");
            }
        }

        /// <summary>
        /// 按绑定前原值还原：只回滚本程序可能改动的 LAYERED / TOPMOST 位与 alpha，
        /// 不整体覆盖 ExStyle（避免把目标自身期间改过的其它样式一并回滚）。
        /// </summary>
        private static void RestoreTargetState(TargetEntry entry)
        {
            IntPtr h = entry.Tracker.TargetHandle;
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return;

            if (!entry.StateCaptured)
            {
                // 捕获失败：退回旧逻辑（alpha 255 + 取消置顶）
                if (entry.LastAlpha != 255) SetTargetAlpha(h, 255, entry.OriginallyLayered ?? false);
                SetTargetTopmost(h, false);
                return;
            }

            try
            {
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                if (entry.OriginalLayered)
                {
                    if ((ex & Native.WS_EX_LAYERED) == 0)
                        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_LAYERED));
                    if (entry.OriginalAlphaKnown)
                        Native.SetLayeredWindowAttributes(h, 0, entry.OriginalAlpha, Native.LWA_ALPHA);
                    Native.InvalidateRect(h, IntPtr.Zero, true);
                }
                else if ((ex & Native.WS_EX_LAYERED) != 0)
                {
                    Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED));
                    Native.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero,
                        Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                }

                bool isTopNow = (Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64()
                    & Native.WS_EX_TOPMOST) != 0;
                if (isTopNow != entry.OriginalTopmost)
                {
                    Native.SetWindowPos(h, entry.OriginalTopmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                        0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"RestoreTargetState failed for 0x{h:X}: {ex2.Message}");
            }
        }

        /// <summary>触发刷新——走 OnUpdate 完整路径（透明度 + 下方垫黑）。</summary>
        private void ApplyMaskNow(TargetEntry e) => e.Tracker.RefreshForeground();

        /// <summary>统一绑定入口（原样搬，托盘气泡改 MessageBox）。</summary>
        private bool BindTarget(TargetInfo info, bool refreshUI = true)
        {
            if (_entries.Any(e => e.Info == info)) return true;

            var boundHandles = new HashSet<IntPtr>(_entries.Select(e => e.Tracker.TargetHandle));
            var (h, matchKind) = TargetTracker.FindMatch(info.WindowTitle, info.ProcessName, boundHandles, info.WindowClass);
            if (h == IntPtr.Zero) return false;
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return false;

            Debug.WriteLine($"BindTarget: [{info}] -> 0x{h:X} (依据:{matchKind})");

            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            CaptureOriginalState(entry, h);
            _entries.Add(entry);
            UpdateBoundHandles();
            entry.Tracker.RefreshNow();
            // 启动时绑定目标 → 自动快照（用户首次启动看到卡片即有图）
            RefreshTargetSnapshot(info);

            if (refreshUI) SyncUI();

            if (!_elevationWarned && !Elevation.IsCurrentProcessElevated())
            {
                bool? elevated = Elevation.IsTargetElevated(h);
                if (elevated == true)
                {
                    _elevationWarned = true;
                    MessageBox.Show("目标窗口以管理员身份运行，而本程序不是。请右键“以管理员身份运行”本程序。",
                        "无法修改此窗口透明度", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            return true;
        }

        /// <summary>统一释放入口（原样搬）。</summary>
        private void ReleaseTarget(TargetEntry entry, string reason, bool updateUI = true)
        {
            Debug.WriteLine($"ReleaseTarget: [{entry.Info}] ({reason})");

            // 按绑定前原值还原（alpha / LAYERED / TOPMOST），不再写死 255 与 NOTOPMOST
            try { RestoreTargetState(entry); } catch { }
            try { entry.Plate.HidePlate(); } catch { }

            try { entry.Tracker.Dispose(); } catch { }
            try { entry.Plate.Dispose(); } catch { }

            _entries.Remove(entry);
            UpdateBoundHandles();

            if (updateUI) SyncUI();
        }

        /// <summary>刷新已绑定句柄快照（仅供 WinEvent 回调线程做事件过滤）。</summary>
        private void UpdateBoundHandles()
        {
            _boundHandles = _entries
                .Select(e => e.Tracker.TargetHandle)
                .Where(h => h != IntPtr.Zero)
                .ToArray();
        }

        /// <summary>统一删除入口：从配置移除 + 解绑（若有）+ 同步 UI + 保存。</summary>
        private void RemoveTarget(TargetInfo info)
        {
            if (SelectedTarget != null && SelectedTarget.Info.Equals(info)) SelectedTarget = null;

            _settings.Targets.Remove(info);
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null) ReleaseTarget(entry, "removed");
            else SyncUI();
            _settings.Save();
        }

        // ════════════════════════════════════════════════════════════════
        // UI 编排（WinForms RebuildTargetList/UpdateUI → XAML 绑定 + 属性通知）
        // ════════════════════════════════════════════════════════════════

        /// <summary>重建卡片集合：按配置顺序，复用既有 VM（保住快照），只刷新绑定状态。</summary>
        private void RebuildTargetList()
        {
            // 移出已不在配置中的 VM
            foreach (var kv in _vmByInfo.ToList())
            {
                if (!_settings.Targets.Contains(kv.Key))
                {
                    _vmByInfo.Remove(kv.Key);
                    Targets.Remove(kv.Value);
                }
            }
            // 按配置顺序补齐 / 刷新
            foreach (var info in _settings.Targets)
            {
                if (_vmByInfo.TryGetValue(info, out var vm))
                {
                    vm.UpdateEntry(FindEntry(info));
                }
                else
                {
                    var nvm = new TargetViewModel(this, info);
                    nvm.UpdateEntry(FindEntry(info));
                    _vmByInfo[info] = nvm;
                    Targets.Add(nvm);
                }
            }
            // 选中目标失效则清空（如删除）
            if (SelectedTarget != null && !_vmByInfo.ContainsKey(SelectedTarget.Info))
                SelectedTarget = null;
        }

        private TargetEntry FindEntry(TargetInfo info) => _entries.FirstOrDefault(e => e.Info == info);

        /// <summary>UI 同步单点：重建目标列表 + 刷新状态栏/徽标/滑块。</summary>
        private void SyncUI()
        {
            try { RebuildTargetList(); } catch { }
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(BadgeText));
            RaiseSliderProperties();
            foreach (var vm in _vmByInfo.Values) { vm.RefreshCardVisuals(); vm.RefreshMeta(); }
            RefreshView();
        }

        private void RaiseSliderProperties()
        {
            OnPropertyChanged(nameof(BackgroundAlpha));
            OnPropertyChanged(nameof(BackgroundAlphaText));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(CornerRadiusText));
            OnPropertyChanged(nameof(GlobalDefaultsText));
            RaiseCustomState();
        }

        // ════════════════════════════════════════════════════════════════
        // 列表视图 / 概览（纯表现层：筛选、搜索、汇总文案）
        // ════════════════════════════════════════════════════════════════

        private bool FilterTarget(object item)
        {
            if (item is not TargetViewModel vm) return false;
            bool stateMatch = _targetFilter switch
            {
                "active" => !vm.IsPending && vm.IsEnabled,   // 停用的卡片不算「生效中」，仍留在「全部」里
                "pending" => vm.IsPending,
                _ => true
            };
            if (!stateMatch) return false;

            var q = (_searchText ?? "").Trim();
            if (q.Length == 0) return true;
            return vm.WindowName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || vm.AliasText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (vm.Info.ProcessName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetFilter(string filter)
        {
            if (_targetFilter == filter) return;
            _targetFilter = filter;
            RefreshView();
        }

        public bool IsFilterAllChecked
        {
            get => _targetFilter == "all";
            set { if (value) SetFilter("all"); }
        }
        public bool IsFilterActiveChecked
        {
            get => _targetFilter == "active";
            set { if (value) SetFilter("active"); }
        }
        public bool IsFilterPendingChecked
        {
            get => _targetFilter == "pending";
            set { if (value) SetFilter("pending"); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? "")) return;
                RefreshView();
            }
        }

        private void RefreshView()
        {
            try { TargetsView?.Refresh(); } catch { }
            OnPropertyChanged(nameof(IsFilterAllChecked));
            OnPropertyChanged(nameof(IsFilterActiveChecked));
            OnPropertyChanged(nameof(IsFilterPendingChecked));
            RaiseOverview();
        }

        private void RaiseOverview()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ActiveSummary));
            OnPropertyChanged(nameof(MissingSummary));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(TargetSubtitle));
            OnPropertyChanged(nameof(FooterTone));
        }

        public int TotalCount => _settings.Targets.Count;
        /// <summary>生效中 = 已绑定且该卡自身启用（全局总闸关闭时仍按单卡状态统计）。</summary>
        public int ActiveCount => _entries.Count(e => e.Info.Enabled);
        public int DisabledCount => _settings.Targets.Count(t => !t.Enabled);
        /// <summary>待激活 = 配置里有、当前找不到窗口（与单卡启停无关）。</summary>
        public int PendingCount => _settings.Targets.Count(t => !_entries.Any(e => e.Info == t));
        public bool IsEmpty => TotalCount == 0;
        public bool HasMissing => _settings.Enabled && PendingCount > 0;

        public string ActiveSummary => !_settings.Enabled
            ? $"已暂停 · {TotalCount} 个目标"
            : TotalCount == 0 ? "暂无绑定目标"
            : DisabledCount > 0 ? $"{ActiveCount} / {TotalCount} 目标生效 · {DisabledCount} 已停用"
            : $"{ActiveCount} / {TotalCount} 目标生效";

        public string MissingSummary => HasMissing ? $"{PendingCount} 个窗口未找到" : "";

        public string TargetSubtitle => TotalCount == 0 ? "WindowTinter · 暂无目标" : $"WindowTinter · {TotalCount} 个目标";

        /// <summary>底栏状态点语义：ok / warn / off（颜色在 XAML 里选）。全部停用时同样给 warn。</summary>
        public string FooterTone => !_settings.Enabled || TotalCount == 0 ? "off"
            : ActiveCount == 0 || PendingCount > 0 ? "warn" : "ok";

        // ── 全局默认面板（底栏上方独立折叠区）──
        public string CustomSummaryText => $"已自定义 {CustomizedCount} 张 · 跟随全局 {FollowingCount} 张";
        public int CustomizedCount => _settings.Targets.Count(t => t.IsCustomized);
        public int FollowingCount => _settings.Targets.Count - CustomizedCount;
        public bool HasCustomized => CustomizedCount > 0;
        public string GlobalDefaultsText => $"全局默认：暗度 {_settings.BackgroundAlpha}% · 圆角 {_settings.CornerRadius}px";

        /// <summary>折叠状态持久化到 settings.json；折叠时内容区 Visibility=Collapsed，不占高度。</summary>
        public bool GlobalPanelExpanded
        {
            get => _settings.GlobalPanelExpanded;
            set
            {
                if (_settings.GlobalPanelExpanded == value) return;
                _settings.GlobalPanelExpanded = value;
                OnPropertyChanged(nameof(GlobalPanelExpanded));
                _settings.Save();
            }
        }

        /// <summary>目标当前生效的暗度：已自定义取卡片自己的值，否则跟随全局默认值。</summary>
        public int EffectiveAlpha(TargetInfo info)
            => info != null && info.IsCustomized ? info.BackgroundAlpha : _settings.BackgroundAlpha;

        /// <summary>目标当前生效的圆角：已自定义取卡片自己的值，否则跟随全局默认值。</summary>
        public int EffectiveCorner(TargetInfo info)
            => info != null && info.IsCustomized ? info.CornerRadius : _settings.CornerRadius;

        /// <summary>首次自定义：先把当前生效的全局值抄进卡片字段，再改被拖动的那一项，避免另一项凭空跳变。</summary>
        private void MarkCustomized(TargetInfo info)
        {
            if (info == null || info.IsCustomized) return;
            info.BackgroundAlpha = _settings.BackgroundAlpha;
            info.CornerRadius = _settings.CornerRadius;
            info.IsCustomized = true;
        }

        private void RaiseCustomState()
        {
            OnPropertyChanged(nameof(CustomizedCount));
            OnPropertyChanged(nameof(FollowingCount));
            OnPropertyChanged(nameof(HasCustomized));
            OnPropertyChanged(nameof(CustomSummaryText));
            OnPropertyChanged(nameof(GlobalDefaultsText));
        }

        // ── 状态文案（对齐 UI_REDESIGN：无 ● 前缀、无 — 破折号，状态由左侧/右侧圆点表达）──
        public string StatusText
        {
            get
            {
                int total = _settings.Targets.Count;
                int active = ActiveCount;
                int pending = PendingCount;
                int disabled = DisabledCount;
                return !_settings.Enabled ? "⏸ 已暂停"
                    : total == 0 ? "等待选择窗口…"
                    : pending > 0 ? $"{active} 个监控中, {pending} 个待激活"
                    : disabled > 0 ? $"{active} 个监控中, {disabled} 个已停用"
                    : $"监控中 {active} 个窗口";
            }
        }

        private static readonly Brush StatusOnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x92, 0x92)));
        private static readonly Brush StatusOffBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));
        public Brush StatusColor => _settings.Enabled ? StatusOnBrush : StatusOffBrush;

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public string BadgeText
        {
            get
            {
                if (_settings.Targets.Count == 0) return "";
                int a = ActiveCount;
                int p = PendingCount;
                string text = p > 0 ? $"{a} 监控 · {p} 待激活" : $"{a} 监控";
                return DisabledCount > 0 ? $"{text} · {DisabledCount} 停用" : text;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 绑定属性：开关 / 复选框 / 滑块（setter 对应 WinForms Actions 各 Toggle*/Set* 方法）
        // ════════════════════════════════════════════════════════════════

        public bool IsEnabled
        {
            get => _settings.Enabled;
            set
            {
                if (_settings.Enabled == value) return;
                _settings.Enabled = value;
                _settings.Save();
                if (value)
                {
                    foreach (var t in _settings.Targets)
                    {
                        if (_entries.Any(e => e.Info == t)) continue;
                        BindTarget(t, refreshUI: false);
                    }
                    RebuildTargetList();
                    foreach (var e in _entries) ApplyMaskNow(e);
                }
                else
                {
                    foreach (var e in _entries) ApplyMaskNow(e);
                }
                SyncUI();
            }
        }

        /// <summary>每行卡片数（1 / 2）。切换即时生效并持久化；双列时选中的卡片自动占满整行。</summary>
        public int CardsPerRow
        {
            get => _settings.CardsPerRow;
            set
            {
                int v = value >= 2 ? 2 : 1;
                if (_settings.CardsPerRow == v) return;
                _settings.CardsPerRow = v;
                _settings.Save();
                OnPropertyChanged(nameof(CardsPerRow));
                OnPropertyChanged(nameof(IsOnePerRow));
                OnPropertyChanged(nameof(IsTwoPerRow));
            }
        }

        public bool IsOnePerRow
        {
            get => CardsPerRow == 1;
            // 点击已选中的分段时会把 IsChecked 置 false：回推一次让绑定重新读源值，避免「看起来取消了」
            set { if (value) CardsPerRow = 1; else OnPropertyChanged(nameof(IsOnePerRow)); }
        }

        public bool IsTwoPerRow
        {
            get => CardsPerRow == 2;
            set { if (value) CardsPerRow = 2; else OnPropertyChanged(nameof(IsTwoPerRow)); }
        }

        public bool BackdropBlackPlate
        {
            get => _settings.BackdropBlackPlate;
            set
            {
                if (_settings.BackdropBlackPlate == value) return;
                _settings.BackdropBlackPlate = value;
                _settings.Save();
                foreach (var e in _entries) e.Tracker.RefreshForeground();
            }
        }

        public bool StartWithWindows
        {
            get => _settings.StartWithWindows;
            set
            {
                if (_settings.StartWithWindows == value) return;
                _settings.StartWithWindows = value;
                _settings.ApplyStartWithWindows();
                _settings.Save();
            }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set
            {
                if (_settings.MinimizeToTray == value) return;
                _settings.MinimizeToTray = value;
                _settings.Save();
            }
        }

        /// <summary>卡片内压暗强度：只作用于当前展开的这张卡，并把它标记为已自定义（此后不再跟随全局）。</summary>
        public int BackgroundAlpha
        {
            get => SelectedTarget == null ? _settings.BackgroundAlpha : EffectiveAlpha(SelectedTarget.Info);
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (SelectedTarget == null) return;
                var info = SelectedTarget.Info;
                if (EffectiveAlpha(info) == v) return;   // 值没变就不置自定义标记
                MarkCustomized(info);
                info.BackgroundAlpha = v;
                var e = FindEntry(info);
                if (e != null) ApplyMaskNow(e);
                OnPropertyChanged(nameof(BackgroundAlpha));
                OnPropertyChanged(nameof(BackgroundAlphaText));
                RaiseCustomState();
                foreach (var vm in _vmByInfo.Values) vm.RefreshMeta();
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string BackgroundAlphaText => $"{BackgroundAlpha}%";

        /// <summary>卡片内圆角：只作用于当前展开的这张卡，并把它标记为已自定义。</summary>
        public int CornerRadius
        {
            get => SelectedTarget == null ? _settings.CornerRadius : EffectiveCorner(SelectedTarget.Info);
            set
            {
                int v = Math.Clamp(value, 0, 32);
                if (SelectedTarget == null) return;
                var info = SelectedTarget.Info;
                if (EffectiveCorner(info) == v) return;  // 值没变就不置自定义标记
                MarkCustomized(info);
                info.CornerRadius = v;
                var e = FindEntry(info);
                if (e != null) SetPlateCornerRadius(e, v);
                OnPropertyChanged(nameof(CornerRadius));
                OnPropertyChanged(nameof(CornerRadiusText));
                RaiseCustomState();
                foreach (var vm in _vmByInfo.Values) vm.RefreshMeta();
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string CornerRadiusText => CornerRadius == 0 ? "关" : $"{CornerRadius}px";

        // ── 全局默认参数（底栏上方折叠面板；只推送到未自定义的卡片）──
        public int GlobalAlpha
        {
            get => _settings.BackgroundAlpha;
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (_settings.BackgroundAlpha == v) return;
                _settings.BackgroundAlpha = v;
                foreach (var e in _entries)
                    if (!e.Info.IsCustomized) ApplyMaskNow(e);
                OnPropertyChanged(nameof(GlobalAlpha));
                OnPropertyChanged(nameof(GlobalAlphaText));
                OnPropertyChanged(nameof(BackgroundAlpha));
                OnPropertyChanged(nameof(BackgroundAlphaText));
                RaiseCustomState();
                foreach (var vm in _vmByInfo.Values) vm.RefreshMeta();
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string GlobalAlphaText => $"{GlobalAlpha}%";

        public int GlobalCorner
        {
            get => _settings.CornerRadius;
            set
            {
                int v = Math.Clamp(value, 0, 32);
                if (_settings.CornerRadius == v) return;
                _settings.CornerRadius = v;
                foreach (var e in _entries)
                    if (!e.Info.IsCustomized) SetPlateCornerRadius(e, v);
                OnPropertyChanged(nameof(GlobalCorner));
                OnPropertyChanged(nameof(GlobalCornerText));
                OnPropertyChanged(nameof(CornerRadius));
                OnPropertyChanged(nameof(CornerRadiusText));
                RaiseCustomState();
                foreach (var vm in _vmByInfo.Values) vm.RefreshMeta();
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string GlobalCornerText => GlobalCorner == 0 ? "直角" : $"{GlobalCorner}px";

        /// <summary>把这张卡恢复成跟随全局（清自定义标记，之后全局改动重新作用到它）。</summary>
        private void RestoreGlobalForSelected()
        {
            var vm = SelectedTarget;
            if (vm == null) return;
            vm.Info.IsCustomized = false;
            var e = FindEntry(vm.Info);
            if (e != null)
            {
                ApplyMaskNow(e);
                SetPlateCornerRadius(e, EffectiveCorner(vm.Info));
            }
            ApplyCustomStateChange();
        }

        /// <summary>一键把全部卡片恢复为跟随全局（用户自定义后唯一回退入口）。</summary>
        public void ResetAllToGlobal()
        {
            bool changed = false;
            foreach (var t in _settings.Targets)
                if (t.IsCustomized) { t.IsCustomized = false; changed = true; }
            if (!changed) return;
            foreach (var e in _entries)
            {
                ApplyMaskNow(e);
                SetPlateCornerRadius(e, _settings.CornerRadius);
            }
            ApplyCustomStateChange();
        }

        /// <summary>自定义状态变化后的统一收尾：刷新滑块显示、卡片文案并立即落盘。</summary>
        private void ApplyCustomStateChange()
        {
            OnPropertyChanged(nameof(BackgroundAlpha));
            OnPropertyChanged(nameof(BackgroundAlphaText));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(CornerRadiusText));
            RaiseCustomState();
            foreach (var vm in _vmByInfo.Values) vm.RefreshMeta();
            _settings.Save();
        }

        /// <summary>恢复出厂默认参数（设置面板「恢复默认」）。</summary>
        private void ResetGlobalDefaults()
        {
            GlobalAlpha = 50;
            GlobalCorner = 14;
            BackdropBlackPlate = true;
        }

        private static void SetPlateCornerRadius(TargetEntry e, int value)
        {
            e.Plate.CornerRadius = value;
            e.Tracker.RefreshForeground();
        }

        // ════════════════════════════════════════════════════════════════
        // 卡片回调（TargetViewModel 命令 → 主 VM）
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 点击卡片：切换展开态——未展开 → 展开并带到前台；已展开 → 收起（再次点击同一张卡）。
        /// 新版卡片是「选中即内联展开」，选中态是唯一的展开入口；
        /// 卡片内滑块改的是这张卡自己的参数（首次改动即置「已自定义」，此后不再跟随全局）。
        /// </summary>
        public void SelectTarget(TargetViewModel vm)
        {
            if (vm == null) return;
            if (ReferenceEquals(SelectedTarget, vm)) { CollapseTarget(); return; }
            SelectedTarget = vm;
            SyncUI();
            var entry = _entries.FirstOrDefault(e => e.Info == vm.Info);
            if (entry != null) BringTargetToTop(entry.Tracker.TargetHandle);
        }

        /// <summary>展开卡片（键盘 Enter/Space）：已展开时保持展开，不切换成收起。</summary>
        public void ExpandTarget(TargetViewModel vm)
        {
            if (vm == null || ReferenceEquals(SelectedTarget, vm)) return;
            SelectTarget(vm);
        }

        /// <summary>
        /// chevron 单独点击：只折叠/展开这张卡。
        /// 不把目标窗口带到前台、不刷新列表视图，因此不会改变列表滚动位置；同一时刻仍只有一张卡展开。
        /// </summary>
        public void ToggleExpandedFromCaret(TargetViewModel vm)
        {
            if (vm == null) return;
            if (ReferenceEquals(SelectedTarget, vm)) CollapseTarget();
            else SelectedTarget = vm;   // setter 已负责逐卡刷新 IsSelected 与滑块可用性
        }

        /// <summary>收起当前展开卡片：纯视图状态，不改目标绑定/滑块值/停用态，也不写 settings.json。</summary>
        public void CollapseTarget()
        {
            if (SelectedTarget == null) return;
            SelectedTarget = null;
        }

        /// <summary>
        /// 单卡启用/停用（卡片右侧开关）。
        /// 停用：只还原该目标的遮罩（底板隐藏 + 原透明度/置顶还原），配置与位置全部保留，卡片留在列表里；
        /// 启用：若尚未绑定则先尝试重绑，再立即刷新效果。全局总闸关闭时只记录状态，不产生视觉变化。
        /// </summary>
        public void SetTargetEnabled(TargetViewModel vm, bool enabled)
        {
            if (vm == null || vm.Info.Enabled == enabled) return;
            vm.Info.Enabled = enabled;
            _settings.Save();

            if (enabled)
            {
                if (!_entries.Any(e => e.Info == vm.Info)) BindTarget(vm.Info, refreshUI: false);
                var entry = FindEntry(vm.Info);
                if (entry != null) ApplyMaskNow(entry);
                RefreshTargetSnapshot(vm.Info);
            }
            else
            {
                var entry = FindEntry(vm.Info);
                if (entry != null) ApplyMaskNow(entry);
            }
            SyncUI();
        }

        /// <summary>重命名（✎）：弹 RenameDialog，空输入 = 清除别名（原样搬 WinForms Actions.RenameTarget）。</summary>
        public void RenameTarget(TargetViewModel vm)
        {
            var dlg = new RenameDialog(vm.Info.Alias) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;
            vm.Info.Alias = dlg.RenameText.Trim();
            _settings.Save();
            SyncUI();
        }

        /// <summary>添加窗口（＋）：弹拾取器，原样搬 WinForms Actions.PickWindow。</summary>
        private void PickWindow()
        {
            var main = Application.Current.MainWindow;
            bool wasVisible = main != null && main.IsVisible;
            if (wasVisible) main.Hide();

            var picker = new WindowPickerWindow();
            bool result = picker.ShowDialog() == true;
            if (wasVisible && main != null) { main.Show(); main.Activate(); }

            if (!result || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            string procName = "";
            try { procName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName ?? ""; } catch { }

            string title = "";
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                title = sb.ToString();
            }
            var info = new TargetInfo
            {
                ProcessName = procName,
                WindowTitle = title,
                WindowClass = TargetTracker.GetWindowClass(picker.SelectedHandle)
            };
            info.BackgroundAlpha = _settings.BackgroundAlpha;
            info.CornerRadius = _settings.CornerRadius;

            if (_settings.Targets.Contains(info))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.Targets.Add(info);

            if (_settings.Enabled)
            {
                if (BindTarget(info))
                {
                    var entry = FindEntry(info);
                    if (entry != null)
                    {
                        BringTargetToTop(entry.Tracker.TargetHandle);
                        ApplyMaskNow(entry);
                    }
                }
                else SyncUI();
            }
            else SyncUI();

            // 新卡片直接展开（选中态 = 展开态），全局统一模式下也一样
            var vm = FindVm(info);
            if (vm != null) SelectedTarget = vm;
            SyncUI();
            // 添加窗口后立即生成快照：BindTarget 内的快照发生在 SyncUI（建卡片 VM）之前，
            // FindVm 为 null 会被丢弃——此处 SyncUI 已完成，必须补一次才能让新卡片立刻出图。
            RefreshTargetSnapshot(info);
        }

        /// <summary>删除目标（×）：从配置移除并解绑。</summary>
        public void RemoveTarget(TargetViewModel vm) => RemoveTarget(vm.Info);

        // ════════════════════════════════════════════════════════════════
        // 快照
        // ════════════════════════════════════════════════════════════════

        /// <summary>是否有卡片快照抓图在进行（工具栏刷新按钮据此只旋转图标，文字保持「刷新」）。</summary>
        public bool IsRefreshing
        {
            get { lock (_snapshotInFlight) return _snapshotInFlight.Count > 0; }
        }

        private void RaiseRefreshing() => OnPropertyChanged(nameof(IsRefreshing));

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
                // 窗口不在：保留上一帧，只把占位文案切到「窗口未找到」
                vm.SetShotFailed(true);
                vm.SetRefreshing(false);
                return;
            }
            IntPtr h = entry.Tracker.TargetHandle;
            lock (_snapshotInFlight)
            {
                if (!_snapshotInFlight.Add(h)) return; // 同一目标已有抓图在跑，本次跳过
            }
            vm.SetRefreshing(true);
            RaiseRefreshing();
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bmp = SnapshotService.Capture(h, 100, 48);
                    if (bmp == null)
                    {
                        // 抓取失败：保留上一帧，占位文案切到「未能抓取」
                        _dispatcher.BeginInvoke(new Action(() => vm.SetShotFailed(true)));
                        return;
                    }
                    try
                    {
                        IntPtr hbit = bmp.GetHbitmap();
                        BitmapSource src;
                        try
                        {
                            src = Imaging.CreateBitmapSourceFromHBitmap(
                                hbit, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally { Native.DeleteObject(hbit); }
                        src.Freeze(); // Freeze 后跨线程安全
                        _dispatcher.BeginInvoke(new Action(() =>
                        {
                            vm.SetShotSource(src);
                            vm.SetShotFailed(false);
                        }));
                    }
                    catch { }
                    finally { bmp.Dispose(); }
                }
                finally
                {
                    lock (_snapshotInFlight) _snapshotInFlight.Remove(h);
                    _dispatcher.BeginInvoke(new Action(() => { vm.SetRefreshing(false); RaiseRefreshing(); }));
                }
            });
        }

        /// <summary>全量刷新所有目标卡片快照（卡头「⟳ 刷新快照」/ 3s 定时）。</summary>
        public void RefreshAllSnapshots()
        {
            foreach (var t in _settings.Targets) RefreshTargetSnapshot(t);
        }

        private TargetViewModel FindVm(TargetInfo info)
            => _vmByInfo.TryGetValue(info, out var vm) ? vm : null;

        // ════════════════════════════════════════════════════════════════
        // WinEvent 全局钩子（原样搬自 MainForm.Events.cs，BeginInvoke→Dispatcher）
        // ════════════════════════════════════════════════════════════════

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;
            var app = Application.Current;
            if (app == null) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var e in _entries) e.Tracker.RefreshForeground();
                    }));
                }
                catch { }
                return;
            }

            // 最小化/恢复：实测事件到达 0–16ms 即可隐藏，而黑块要等动画结束 ~175ms 才暴露。
            // 事件丢失时由 TargetTracker 的 500ms 轮询（IsIconic）兜底。
            if (eventType is Native.EVENT_SYSTEM_MINIMIZESTART or Native.EVENT_SYSTEM_MINIMIZEEND)
            {
                var minHwnd = hwnd;
                if (Array.IndexOf(_boundHandles, minHwnd) < 0) return;
                bool start = eventType == Native.EVENT_SYSTEM_MINIMIZESTART;
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == minHwnd);
                        if (match == null) return;
                        if (start)
                        {
                            match.MinimizeSuppressed = true;
                            match.MinimizeSuppressedAtMs = Environment.TickCount64;
                            match.Plate.HidePlate();
                        }
                        else
                        {
                            match.MinimizeSuppressed = false;
                            match.Tracker.RefreshNow();
                        }
                    }));
                }
                catch { }
                return;
            }

            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_REORDER
                               or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                // 先在本线程按句柄过滤：桌面级 LOCATIONCHANGE 量很大，无关窗口不投递到 UI 线程
                if (Array.IndexOf(_boundHandles, targetHwnd) < 0) return;
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                        if (match == null) return;
                        if (eventType == Native.EVENT_OBJECT_DESTROY)
                            ReleaseTarget(match, "destroyed");
                        else
                        {
                            match.Tracker.RefreshNow();
                            // 目标窗口显示/启动 → 自动快照（用户运行时新启动的目标首次可见即有图）
                            if (eventType == Native.EVENT_OBJECT_SHOW)
                                RefreshTargetSnapshot(match.Info);
                        }
                    }));
                }
                catch { }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 命令（操作栏 / 卡头）
        // ════════════════════════════════════════════════════════════════

        public RelayCommand RefreshSnapshotsCommand { get; private set; }  // 卡头 ⟳
        public RelayCommand AddWindowCommand { get; private set; }         // + 添加窗口（D 阶段：WindowPickerWindow）
        public RelayCommand SaveCommand { get; private set; }              // 💾 保存配置
        public RelayCommand ExitCommand { get; private set; }              // 🚪 退出
        public RelayCommand OpenConfigFolderCommand { get; private set; }  // 📂 配置文件夹
        public RelayCommand AboutCommand { get; private set; }             // ℹ 关于
        public RelayCommand OpenSettingsCommand { get; private set; }      // 设置面板
        public RelayCommand RestoreGlobalCommand { get; private set; }     // 卡片「恢复全局」
        public RelayCommand ResetAllToGlobalCommand { get; private set; }  // 全局面板「全部恢复跟随全局」
        public RelayCommand ResetDefaultsCommand { get; private set; }     // 设置「恢复默认」

        private void BuildCommands()
        {
            RefreshSnapshotsCommand = new RelayCommand(RefreshAllSnapshots);
            AddWindowCommand = new RelayCommand(PickWindow);
            SaveCommand = new RelayCommand(() => _settings.Save());
            ExitCommand = new RelayCommand(ExitApplication);
            OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
            AboutCommand = new RelayCommand(ShowAbout);
            OpenSettingsCommand = new RelayCommand(ShowSettings);
            RestoreGlobalCommand = new RelayCommand(RestoreGlobalForSelected);
            ResetAllToGlobalCommand = new RelayCommand(ResetAllToGlobal);
            ResetDefaultsCommand = new RelayCommand(ResetGlobalDefaults);
        }

        private void ShowSettings()
        {
            var owner = Application.Current.MainWindow;
            // 托盘菜单唤起时主窗口可能已隐藏——先显示，避免模态面板无可见宿主
            if (owner != null && !owner.IsVisible)
            {
                owner.ShowInTaskbar = true;
                owner.Show();
                owner.WindowState = WindowState.Normal;
            }
            var dlg = new SettingsDialog { Owner = owner, DataContext = this };
            dlg.ShowDialog();
        }

        /// <summary>真实退出标记：MainWindow.Closing 据此走"释放+退出"而非"最小化到托盘"。</summary>
        public bool ReallyQuit { get; set; }

        /// <summary>托盘驻留时的保存（不释放效果）。</summary>
        public void SaveSettings() => _settings.Save();

        /// <summary>暴露 Settings 引用（用于窗口状态保存/恢复，View 直接读写字段）。</summary>
        public Settings GetSettings() => _settings;

        /// <summary>退出：置真实退出标记 → 关主窗（Closing 兜底释放全部）。</summary>
        public void ExitApplication()
        {
            ReallyQuit = true;
            var w = Application.Current.MainWindow;
            if (w != null) w.Close();
            else { Shutdown(); Application.Current.Shutdown(); }
        }

        /// <summary>窗体关闭兜底（MainWindow.Closing）：不托盘时直接退出；托盘逻辑 D 阶段接入。</summary>
        public void Shutdown()
        {
            _autoBindTimer?.Stop();
            _saveDebounceTimer?.Stop();
            if (_winEventHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            foreach (var e in _entries.ToList())
            {
                try { ReleaseTarget(e, "quit", updateUI: false); } catch { }
            }
            _settings.Save();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            var dlg = new AboutDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }
    }
}
