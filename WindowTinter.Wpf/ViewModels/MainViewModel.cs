using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WindowTinter.Views;

namespace WindowTinter.ViewModels
{
    /// <summary>
    /// 主 VM：目标生命周期（绑定/释放/删除/全量解绑）+ 效果应用（透明度+黑底）+ UI 状态。
    /// 铁律：领域逻辑（BindTarget/ReleaseTarget/ApplyEntryEffect/SetTargetAlpha/SetTargetTopmost/
    /// BringTargetToTop/RemoveTarget/UnbindAll）原样搬自 WinForms 版 MainForm，不改逻辑；
    /// 仅容器适配：Timer→DispatcherTimer、BeginInvoke→Dispatcher.BeginInvoke、托盘气泡→MessageBox（D 阶段换回托盘）。
    /// </summary>
    internal class MainViewModel : ObservableObject
    {
        private readonly Settings _settings = Settings.Load();
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, TargetViewModel> _vmByInfo = new();

        public ObservableCollection<TargetViewModel> Targets { get; } = new();

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
        private readonly DispatcherTimer _snapshotTimer;    // 3s 快照定时刷新

        // ── WinEvent 钩子（前台切换/移动/销毁即时响应）──
        private Native.WinEventProc _winEventProc;
        private IntPtr _winEventHook = IntPtr.Zero;

        private bool _elevationWarned;

        public MainViewModel()
        {
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
            if (!_settings.GlobalTransparency && SelectedTarget == null)
                SelectedTarget = Targets.FirstOrDefault();

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

            _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _snapshotTimer.Tick += (_, _) => { if (_settings.Enabled) RefreshAllSnapshots(); };
            _snapshotTimer.Start();

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
            plate.CornerRadius = _settings.GlobalCornerRadius ? _settings.CornerRadius : info.CornerRadius;
            tracker.OnUpdate += (r, visible) => ApplyEntryEffect(entry, visible);
            return entry;
        }

        /// <summary>效果应用（唯一入口）：差异计算 alpha/黑底，有变化才落系统调用。</summary>
        private void ApplyEntryEffect(TargetEntry entry, bool visible)
        {
            IntPtr h = entry.Tracker.TargetHandle;
            if (h == IntPtr.Zero) return;

            int bgPct = _settings.GlobalTransparency ? _settings.BackgroundAlpha : entry.Info.BackgroundAlpha;

            if (!_settings.Enabled || !visible)
            {
                entry.Plate.HidePlate();
                SetTargetTopmost(h, false);
                if (entry.LastAlpha != 255)
                {
                    SetTargetAlpha(h, 255, entry.OriginallyLayered ?? false);
                    entry.LastAlpha = 255;
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
            if (_settings.BackdropBlackPlate)
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

                Native.SetWindowPos(hwnd, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
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
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED));
                        Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                    }
                    else if (hasLayered)
                    {
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
            _entries.Add(entry);
            entry.Tracker.RefreshNow();
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

            try { entry.Tracker.Dispose(); } catch { }
            try { entry.Plate.Dispose(); } catch { }

            _entries.Remove(entry);

            if (updateUI) SyncUI();
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

        /// <summary>全部解绑（保留配置，转待激活）。</summary>
        private void UnbindAll()
        {
            foreach (var e in _entries.ToList())
                ReleaseTarget(e, "unbind", updateUI: false);
            SyncUI();
        }

        /// <summary>重新查找 = 全量重绑定（原样搬）。</summary>
        private void RefindAllWindows()
        {
            UnbindAll();
            foreach (var t in _settings.Targets)
                BindTarget(t, refreshUI: false);
            SyncUI();
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
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(BadgeText));
            RaiseSliderProperties();
        }

        private void RaiseSliderProperties()
        {
            OnPropertyChanged(nameof(BackgroundAlpha));
            OnPropertyChanged(nameof(BackgroundAlphaText));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(CornerRadiusText));
            OnPropertyChanged(nameof(IsAlphaSliderEnabled));
            OnPropertyChanged(nameof(IsCornerSliderEnabled));
        }

        // ── 状态文案（对齐 WinForms GetStatusText / UpdateTargetBadge）──
        public string StatusText
        {
            get
            {
                int total = _settings.Targets.Count;
                int active = _entries.Count;
                int pending = total - active;
                return !_settings.Enabled ? "⏸ 已暂停"
                    : total == 0 ? "○ 等待选择窗口…"
                    : pending > 0 ? $"● {active} 个监控中, {pending} 个待激活"
                    : $"● 监控中 — {active} 个窗口";
            }
        }

        public Brush StatusColor => _settings.Enabled ? new SolidColorBrush(Color.FromRgb(0x3E, 0xCF, 0x8E)) : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        public string BadgeText
        {
            get
            {
                int a = _entries.Count;
                int p = _settings.Targets.Count - a;
                return _settings.Targets.Count == 0 ? ""
                    : p > 0 ? $"● {a} 监控 · {p} 待激活" : $"● {a} 监控";
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

        public bool GlobalTransparency
        {
            get => _settings.GlobalTransparency;
            set
            {
                if (_settings.GlobalTransparency == value) return;
                _settings.GlobalTransparency = value;
                if (!value)
                {
                    foreach (var t in _settings.Targets) t.BackgroundAlpha = _settings.BackgroundAlpha;
                    SelectedTarget = Targets.FirstOrDefault();
                }
                _settings.Save();
                SyncUI();
                foreach (var e in _entries) ApplyMaskNow(e);
            }
        }

        public bool GlobalCornerRadius
        {
            get => _settings.GlobalCornerRadius;
            set
            {
                if (_settings.GlobalCornerRadius == value) return;
                _settings.GlobalCornerRadius = value;
                if (!value)
                {
                    foreach (var t in _settings.Targets) t.CornerRadius = _settings.CornerRadius;
                    SelectedTarget = Targets.FirstOrDefault();
                }
                _settings.Save();
                SyncUI();
                foreach (var e in _entries)
                {
                    bool useGlobal = _settings.GlobalCornerRadius;
                    e.Plate.CornerRadius = useGlobal ? _settings.CornerRadius : e.Info.CornerRadius;
                    e.Tracker.RefreshForeground();
                }
            }
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

        public int BackgroundAlpha
        {
            get => _settings.GlobalTransparency ? _settings.BackgroundAlpha : (SelectedTarget?.Info.BackgroundAlpha ?? 0);
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (_settings.GlobalTransparency) _settings.BackgroundAlpha = v;
                else { if (SelectedTarget == null) return; SelectedTarget.Info.BackgroundAlpha = v; }
                foreach (var e in _entries) ApplyMaskNow(e);
                OnPropertyChanged(nameof(BackgroundAlpha));
                OnPropertyChanged(nameof(BackgroundAlphaText));
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string BackgroundAlphaText => $"{BackgroundAlpha}%";

        public int CornerRadius
        {
            get => _settings.GlobalCornerRadius ? _settings.CornerRadius : (SelectedTarget?.Info.CornerRadius ?? 0);
            set
            {
                int v = Math.Clamp(value, 0, 20);
                if (_settings.GlobalCornerRadius)
                {
                    _settings.CornerRadius = v;
                    foreach (var e in _entries)
                        SetPlateCornerRadius(e, v);
                }
                else
                {
                    if (SelectedTarget == null) return;
                    SelectedTarget.Info.CornerRadius = v;
                    foreach (var e in _entries)
                    {
                        if (e.Info == SelectedTarget.Info)
                        {
                            SetPlateCornerRadius(e, v);
                            break;
                        }
                    }
                }
                OnPropertyChanged(nameof(CornerRadius));
                OnPropertyChanged(nameof(CornerRadiusText));
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        public string CornerRadiusText => CornerRadius == 0 ? "关" : $"{CornerRadius}px";

        public bool IsAlphaSliderEnabled => _settings.GlobalTransparency || SelectedTarget != null;
        public bool IsCornerSliderEnabled => _settings.GlobalCornerRadius || SelectedTarget != null;

        private static void SetPlateCornerRadius(TargetEntry e, int value)
        {
            e.Plate.CornerRadius = value;
            e.Tracker.RefreshForeground();
        }

        // ════════════════════════════════════════════════════════════════
        // 卡片回调（TargetViewModel 命令 → 主 VM）
        // ════════════════════════════════════════════════════════════════

        /// <summary>点 ○/● 选中目标：非全局模式下生效，一次性带到前台便于查看效果。</summary>
        public void SelectTarget(TargetViewModel vm)
        {
            if (_settings.GlobalTransparency && _settings.GlobalCornerRadius) return;
            SelectedTarget = vm;
            SyncUI();
            var entry = _entries.FirstOrDefault(e => e.Info == vm.Info);
            if (entry != null) BringTargetToTop(entry.Tracker.TargetHandle);
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

            if (!_settings.GlobalTransparency || !_settings.GlobalCornerRadius)
            {
                var vm = FindVm(info);
                if (vm != null) SelectedTarget = vm;
            }
            SyncUI();
        }

        /// <summary>删除目标（×）：从配置移除并解绑。</summary>
        public void RemoveTarget(TargetViewModel vm) => RemoveTarget(vm.Info);

        // ════════════════════════════════════════════════════════════════
        // 快照
        // ════════════════════════════════════════════════════════════════

        /// <summary>刷新单张卡片快照（绑定即时 / 移动即时 / 定时 / 手动全量）。卡片快照区约 100×48。</summary>
        public void RefreshTargetSnapshot(TargetInfo info)
        {
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            var vm = FindVm(info);
            var bmp = entry != null ? SnapshotService.Capture(entry.Tracker.TargetHandle, 100, 48) : null;
            if (vm == null) { bmp?.Dispose(); return; }
            vm.SetShot(bmp); // 内部消费并释放
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

            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_REORDER
                               or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
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
                            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_SHOW)
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
        public RelayCommand RefindAllCommand { get; private set; }         // ↻ 重新查找
        public RelayCommand SaveCommand { get; private set; }              // 💾 保存配置
        public RelayCommand ExitCommand { get; private set; }              // 🚪 退出
        public RelayCommand OpenConfigFolderCommand { get; private set; }  // 📂 配置文件夹
        public RelayCommand AboutCommand { get; private set; }             // ℹ 关于

        private void BuildCommands()
        {
            RefreshSnapshotsCommand = new RelayCommand(RefreshAllSnapshots);
            AddWindowCommand = new RelayCommand(PickWindow);
            RefindAllCommand = new RelayCommand(RefindAllWindows);
            SaveCommand = new RelayCommand(() => _settings.Save());
            ExitCommand = new RelayCommand(ExitApplication);
            OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
            AboutCommand = new RelayCommand(ShowAbout);
        }

        /// <summary>真实退出标记：MainWindow.Closing 据此走"释放+退出"而非"最小化到托盘"。</summary>
        public bool ReallyQuit { get; set; }

        /// <summary>托盘驻留时的保存（不释放效果）。</summary>
        public void SaveSettings() => _settings.Save();

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
            _snapshotTimer?.Stop();
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
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string v = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.6.0";
            MessageBox.Show(
                $"暗幕 v{v}\n\n" +
                "给任意窗口设置透明度、并在其正下方垫纯黑的常驻小工具。\n" +
                "支持多窗口同时控制。\n\n" +
                "• 配置: 与 exe 同目录 WindowTinter.settings.json\n" +
                "• 图标: app.ico 与 exe 同目录\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }
    }
}
