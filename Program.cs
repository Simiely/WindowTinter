using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置的自定义 TrackBar。自带底部视觉裁剪。</summary>
    internal class JumpTrackBar : TrackBar
    {
        public JumpTrackBar()
        {
            HandleCreated += (_, _) => ClipVisual();
            Resize += (_, _) => ClipVisual();
        }

        /// <summary>裁掉 TrackBar 原生控件底部的视觉溢出（轨道背景比声明区域大）。</summary>
        private void ClipVisual()
        {
            if (Width > 0 && Height > 0)
            {
                int clipH = Math.Max(Height - 8, 12);
                this.Region?.Dispose();
                this.Region = new Region(new Rectangle(0, 0, Width, clipH));
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDOWN)
            {
                int x = unchecked((short)((int)m.LParam & 0xFFFF));
                int channelW = Width - 24;
                if (channelW > 0)
                {
                    int newVal = (x - 12) * (Maximum - Minimum) / channelW + Minimum;
                    newVal = Math.Clamp(newVal, Minimum, Maximum);
                    Value = newVal; // 先跳到鼠标位置，再让默认 WndProc 处理拖拽
                }
            }
            base.WndProc(ref m);
        }
    }

    internal partial class MainForm : Form
    {
        // ── 核心状态 ──────────────────────────────────────────────

        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, Panel> _pendingPanels = new();
        private readonly Dictionary<TargetInfo, Button> _selectButtons = new();
        private TargetInfo _selectedTarget;   // 非全局模式下，滑块当前编辑的目标

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;
        private Timer _onShownTimer;
        private Timer _saveDebounceTimer;
        private Icon _appIcon;
        private float _dpiScale = 1f;   // 系统 DPI 缩放比（DeviceDpi/96）；用于手动换算运行时动态添加的面板尺寸

        private static readonly string AppVersion = GetAppVersion();
        private static string GetAppVersion()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.5.2";
        }

        // ── UI 控件 ────────────────────────────────────────────────

        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbBgAlpha;
        private Label _lblBgAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkBackdropPlate;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkGlobalTransparency;
        private CheckBox _chkGlobalCornerRadius;
        private TrackBar _tbCornerRadius;
        private Label _lblCornerRadius;

        // ── 窗口 ───────────────────────────────────────────────────

        public MainForm(bool startHidden = false)
        {
            // Win11 高 DPI：以 96 DPI 为设计基准，运行时由 AutoScaleMode.Dpi 按屏幕真实 DPI 自动缩放全部静态控件。
            // 关键：控件必须在 CreateControl（即本构造函数）阶段建好，自动缩放才会作用于它们——
            // 因此 BuildUI() 放在这里而非 OnLoad。BlackPlate 是物理像素分层窗，单独用 AutoScaleMode.None，不受影响。
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            _settings = Settings.Load();

            Text = $"暗幕 v{AppVersion}";
            var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
            try { _appIcon = File.Exists(iconPath) ? new Icon(iconPath) : null; }
            catch { _appIcon = null; }
            Icon = _appIcon; // 图标缺失/损坏时退化为系统默认图标，避免启动崩溃
            ClientSize = new Size(470, 695);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // 开机自启：直接最小化到托盘（WindowState 在句柄创建前设置，窗口以最小化态创建，无闪烁），
            // 且不显示任务栏按钮——仅托盘图标驻留。用户双击托盘即可打开设置窗口。
            if (startHidden)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
            Load += OnLoad;
            Shown += OnShown;
            Activated += OnActivated;
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => Quit();

            BuildUI();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            // 句柄已创建，DeviceDpi 为真实屏幕 DPI（进程已 PerMonitorV2 感知）。
            // 静态控件已由 AutoScaleMode.Dpi 自动缩放；运行时动态添加的面板按此因子换算尺寸。
            _dpiScale = Math.Max(DeviceDpi / 96f, 1f);

            // 启动时清除上次强制退出可能残留的透明效果
            RestoreAllTargets();

            BuildTray();
            InstallWinEventHook();

            // 兜底定时器（3s）：只做"尝试绑定未绑定目标"；条目清理由 WinEvent 事件驱动（见 WinEventProcCallback）
            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    BindTarget(t);
                }
            };
            _autoBindTimer.Start();

            // 滑块去抖：拖动停止 200ms 后写盘
            _saveDebounceTimer = new Timer { Interval = 200 };
            _saveDebounceTimer.Tick += (_, _) => { _saveDebounceTimer.Stop(); _settings.Save(); };

            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    BindTarget(t); // BindTarget 内部已 RebuildTargetList，无需手动同步待激活面板
                }
            }

            // 非全局模式下，默认选中第一个目标
            if (!_settings.GlobalTransparency && _selectedTarget == null)
                _selectedTarget = _settings.Targets.FirstOrDefault();

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        private void OnShown(object _, EventArgs __)
        {
            foreach (var e in _entries) e.Tracker.RefreshNow();
            // 用一次性定时器推迟 100ms，确保消息泵完整运转后 UpdateLayeredWindow 稳定
            _onShownTimer = new Timer { Interval = 100 };
            _onShownTimer.Tick += (s, args) => { _onShownTimer.Stop(); _onShownTimer.Dispose(); foreach (var e in _entries) ApplyMaskNow(e); };
            _onShownTimer.Start();
        }

        /// <summary>点击 WindowTinter 自身时 WinEvent SKIPOWNPROCESS 会跳过前台事件，
        /// 通过 Activated 补发 RefreshForeground 让目标切到透明状态。
        /// _inActivated 防 BeginInvoke 嵌套重入（Activated 可能连续触发）。</summary>
        private bool _inActivated;
        private void OnActivated(object _, EventArgs __)
        {
            if (_inActivated) return;
            _inActivated = true;
            BeginInvoke(new Action(() =>
            {
                try { foreach (var e in _entries) e.Tracker.RefreshForeground(); }
                finally { _inActivated = false; }
            }));
        }

        /// <summary>启动时遍历所有已配置目标，恢复透明度——处理上次强制杀进程残留。</summary>
        private void RestoreAllTargets()
        {
            foreach (var t in _settings.Targets)
            {
                var h = TargetTracker.FindByTitleAndProcess(t.WindowTitle, t.ProcessName, null, t.WindowClass);
                if (h != IntPtr.Zero)
                {
                    SetTargetAlpha(h, 255, true); // 启动恢复：保留 layered 分支，不破坏原生分层窗口
                    SetTargetTopmost(h, false); // 清理上次强制退出可能残留的置顶
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 目标条目管理
        // ════════════════════════════════════════════════════════════

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public BlackPlate Plate;
            public Panel UIPanel;
            public bool IsBound;                 // 是否处于"监控中"状态
            public byte LastAlpha = 255;         // 最近一次应用到目标窗口的 alpha（差异计算用）
            public bool? OriginallyLayered;      // 目标窗口原生是否 WS_EX_LAYERED（首次置透明前记录，用于恢复时决定是否可移除该样式）
        }

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

        // ── 提权检测：目标窗口以管理员运行、本程序非提权时无法修改其透明度 ──

        private bool _elevationWarned;

        private static bool? IsTargetElevated(IntPtr hwnd)
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero) return null;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return null;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return null;
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, (uint)p.Id);
                if (hProc == IntPtr.Zero) return false;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return false;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return false;
        }

        /// <summary>触发刷新——走 OnUpdate 完整路径（透明度 + 下方垫黑）。</summary>
        private void ApplyMaskNow(TargetEntry e)
        {
            e.Tracker.RefreshForeground();
        }

        /// <summary>统一绑定入口：查找目标窗口 → 创建条目 → 挂 UI → 生效。返回是否绑定成功。</summary>
        private bool BindTarget(TargetInfo info)
        {
            if (_entries.Any(e => e.Info == info)) return true; // 已绑定

            var boundHandles = new HashSet<IntPtr>(_entries.Select(e => e.Tracker.TargetHandle));
            var (h, matchKind) = TargetTracker.FindMatch(info.WindowTitle, info.ProcessName, boundHandles, info.WindowClass);
            if (h == IntPtr.Zero) return false;
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return false; // 该窗口已被其它目标占用

            Debug.WriteLine($"BindTarget: [{info}] -> 0x{h:X} (依据:{matchKind})");

            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            entry.IsBound = true;
            _entries.Add(entry);
            RebuildTargetList();
            UpdateUI();

            // 提权提示：目标以管理员身份运行而本程序未提权时，改透明度会静默失败
            if (!_elevationWarned && !IsCurrentProcessElevated())
            {
                bool? elevated = IsTargetElevated(h);
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
            entry.IsBound = false;

            _entries.Remove(entry);

            // 3) 同步 UI（窗体关闭等场景可跳过）
            if (updateUI)
            {
                try { RebuildTargetList(); } catch { }
                UpdateUI();
            }
        }

        /// <summary>统一删除入口：从配置移除 + 解绑（若有）+ 同步 UI + 保存。</summary>
        private void RemoveTarget(TargetInfo info)
        {
            _selectButtons.Remove(info);
            if (_selectedTarget != null && _selectedTarget.Equals(info)) _selectedTarget = null;

            _settings.Targets.Remove(info); // 先移出配置，后续 Rebuild 才不会残留该目标面板
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null) ReleaseTarget(entry, "removed");
            else RebuildTargetList();
            _settings.Save();
        }

        /// <summary>
        /// 按状态统一重建目标列表 UI（活跃 + 待激活，按配置顺序）。
        /// 取代原先 AddTargetUI/AddPendingUI 双份构建与 SetChildIndex 排序 hack。
        /// </summary>
        private void RebuildTargetList()
        {
            _pnlTargets.Controls.Clear();
            _pendingPanels.Clear();
            _selectButtons.Clear();

            foreach (var t in _settings.Targets)
            {
                var entry = _entries.FirstOrDefault(e => e.Info == t);
                var pnl = CreateTargetPanel(t, pending: entry == null);
                if (entry != null) entry.UIPanel = pnl;
                else _pendingPanels[t] = pnl;
                _pnlTargets.Controls.Add(pnl);
            }
            UpdateSelectButtons();
        }

        /// <summary>创建单个目标面板（活跃或待激活）。</summary>
        private Panel CreateTargetPanel(TargetInfo info, bool pending)
        {
            int w = _pnlTargets.ClientSize.Width - (int)(6 * _dpiScale);
            bool sel = !_settings.GlobalTransparency && _selectedTarget != null && _selectedTarget.Equals(info);
            var pnl = new Panel { Size = new Size(w, (int)(32 * _dpiScale)), Margin = new Padding(0, 0, 0, (int)(3 * _dpiScale)),
                BackColor = sel ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40),
                Cursor = Cursors.Hand };
            pnl.Click += (_, _) => SelectTarget(info);

            var lbl = new Label
            {
                Text = pending ? $"  ⏳ 待激活 — {info}" : $"  {info}",
                AutoSize = true,
                Location = new Point((int)(4 * _dpiScale), (int)(8 * _dpiScale)),
                MaximumSize = new Size((int)(250 * _dpiScale), (int)(20 * _dpiScale)),
                ForeColor = pending ? Color.FromArgb(120, 120, 120) : Color.FromArgb(224, 224, 224),
                Cursor = Cursors.Hand
            };
            lbl.Click += (_, _) => SelectTarget(info);
            pnl.Controls.Add(lbl);

            bool btnEnabled = !_settings.GlobalTransparency;
            var btnSelect = CreateSelectButton(w, sel, btnEnabled, (_, _) => SelectTarget(info));
            pnl.Controls.Add(btnSelect);
            _selectButtons[info] = btnSelect;

            var btnRemove = CreateRemoveButton(w, (_, _) => RemoveTarget(info));
            pnl.Controls.Add(btnRemove);

            return pnl;
        }

        /// <summary>全部解绑（保留配置，转待激活）。供"重新查找"使用。</summary>
        private void UnbindAll()
        {
            foreach (var e in _entries.ToList())
                ReleaseTarget(e, "unbind"); // 每次 Release 都会 RebuildTargetList，最终全部呈现"待激活"
        }

        private void Quit()
        {
            _settings.Save();
            _reallyQuit = true;
            _autoBindTimer?.Stop(); _autoBindTimer?.Dispose();
            _onShownTimer?.Stop(); _onShownTimer?.Dispose();
            _saveDebounceTimer?.Stop(); _saveDebounceTimer?.Dispose();
            _tray.Visible = false;  // 先隐藏托盘（内部会访问 Icon.Handle）
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            // 统一走 ReleaseTarget 清理（不转待激活、不刷新 UI——窗体即将关闭）
            foreach (var e in _entries.ToList())
            {
                try { ReleaseTarget(e, "quit", updateUI: false); }
                catch { /* 单条清理失败不阻塞后续 */ }
            }
            _pendingPanels.Clear();
            _appIcon?.Dispose();  // 最后释放——NotifyIcon 已不再引用它
        }

        // ════════════════════════════════════════════════════════════
        // 入口
        // ════════════════════════════════════════════════════════════

        [STAThread]
        static void Main()
        {
            // PerMonitorV2 必须在 EnableVisualStyles 之前设置，否则静默失效（进程退化为系统 DPI 感知，
            // DeviceDpi 永远返回 96，主界面无法按缩放比缩放）。csproj 的 ApplicationHighDpiMode 也会注入相同声明作为兜底。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 开机自启（注册表 Run 项带 /startup 参数）时只驻留托盘、不弹主窗口；
            // 手动双击 exe 不带参数，则正常显示主窗口。
            bool startHidden = Environment.GetCommandLineArgs().Skip(1)
                .Any(a => a.Equals("/startup", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/background", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(startHidden));
        }
    }
}
