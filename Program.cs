using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置的自定义 TrackBar。</summary>
    internal class JumpTrackBar : TrackBar
    {
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

    internal class MainForm : Form
    {
        // ── 核心状态 ──────────────────────────────────────────────

        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, Panel> _pendingPanels = new();

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;
        private Timer _onShownTimer;
        private Icon _appIcon;
        private bool _previewMask; // Alpha 滑块拖拽中——强制所有条目显示蒙版

        // ── UI 控件 ────────────────────────────────────────────────

        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbAlpha;
        private Label _lblAlpha;
        private TrackBar _tbBgAlpha;
        private Label _lblBgAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkKeepTransparency;
        private CheckBox _chkMinimizeTray;

        // ── 窗口 ───────────────────────────────────────────────────

        public MainForm()
        {
            _settings = Settings.Load();

            Text = "暗幕 v3.6.2";
            var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
            _appIcon = new Icon(iconPath);
            Icon = _appIcon;
            ClientSize = new Size(470, 608);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Load += OnLoad;
            Shown += OnShown;
            Activated += OnActivated;
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => Quit();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            // 启动时清除上次强制退出可能残留的透明效果
            RestoreAllTargets();

            BuildTray();
            BuildUI();
            InstallWinEventHook();

            // 3 秒一次检查是否有目标窗口新启动但未绑定
            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;

                // 清理窗口已销毁的条目——放回待激活列表
                bool anyChange = false;
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];
                    // 防御性保护：IsWindow(Zero) 返回 false，但保留判零以确保意图明确
                    if (!Native.IsWindow(e.Tracker.TargetHandle) && e.Tracker.TargetHandle != IntPtr.Zero)
                    {
                        e.MaskDark.HideMask(); e.Tracker.Dispose(); e.MaskDark.Dispose();
                        _entries.RemoveAt(i);
                        if (e.UIPanel != null) _pnlTargets.Controls.Remove(e.UIPanel);
                        AddPendingUI(e.Info); // 放回待激活
                        anyChange = true;
                    }
                }

                // 查找未绑定目标（包括待激活目标）
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    var entry = _entries.FirstOrDefault(e => e.Info == t);
                    if (entry != null)
                    {
                        RemovePendingUI(t);
                        ApplyMaskNow(entry);
                        anyChange = true;
                    }
                }
                if (anyChange) BeginInvoke(() => UpdateUI());
            };
            _autoBindTimer.Start();

            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets)
                    TryBindTarget(t);
                // 移除已成功绑定的待激活面板
                foreach (var entry in _entries)
                    RemovePendingUI(entry.Info);
            }

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
                var h = TargetTracker.FindByTitleAndProcess(t.WindowTitle, t.ProcessName);
                if (h != IntPtr.Zero)
                    SetTargetAlpha(h, 255);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 目标条目管理
        // ════════════════════════════════════════════════════════════

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public MaskOverlay MaskDark;
            public Panel UIPanel;
        }

        /// <summary>创建条目并挂载 OnUpdate——所有蒙版显示逻辑的唯一入口。</summary>
        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var maskDark = new MaskOverlay();

            byte _lastBgAlpha = 255;

            tracker.OnUpdate += (r, visible) =>
            {
                bool fg = Native.GetForegroundWindow() == tracker.TargetHandle;
                if (!_settings.Enabled || !visible)
                {
                    maskDark.HideMask();
                    if (_lastBgAlpha != 255) { SetTargetAlpha(tracker.TargetHandle, 255); _lastBgAlpha = 255; }
                    return;
                }

                // 保持透明度模式：不用蒙版，始终用 BackgroundAlpha
                if (_settings.KeepTransparency)
                {
                    maskDark.HideMask();
                    byte targetAlpha = (byte)((100 - _settings.BackgroundAlpha) * 255 / 100);
                    if (_lastBgAlpha != targetAlpha)
                    {
                        SetTargetAlpha(tracker.TargetHandle, targetAlpha);
                        _lastBgAlpha = targetAlpha;
                    }
                    return;
                }

                if (fg || _previewMask)
                {
                    maskDark.Alpha = (byte)(_settings.Alpha * 255 / 100);
                    maskDark.AlignTo(r);
                    if (_lastBgAlpha != 255)
                    {
                        var hwnd = tracker.TargetHandle;
                        if (_previewMask)
                        {
                            // 拖拽预览——立即恢复全不透明，让蒙版效果纯净可见
                            SetTargetAlpha(hwnd, 255);
                            _lastBgAlpha = 255;
                        }
                        else
                        {
                            // 正常前台切换——延迟一帧避免闪白
                            BeginInvoke(() =>
                            {
                                if (Native.IsWindow(hwnd) && Native.GetForegroundWindow() == hwnd)
                                    SetTargetAlpha(hwnd, 255);
                            });
                            _lastBgAlpha = 255;
                        }
                    }
                }
                else
                {
                    byte targetAlpha = (byte)((100 - _settings.BackgroundAlpha) * 255 / 100);
                    if (_lastBgAlpha != targetAlpha)
                    {
                        SetTargetAlpha(tracker.TargetHandle, targetAlpha);
                        _lastBgAlpha = targetAlpha;
                    }
                    maskDark.HideMask();
                }
            };

            return new TargetEntry { Info = info, Tracker = tracker, MaskDark = maskDark };
        }

        private static void SetTargetAlpha(IntPtr hwnd, byte alpha)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                if (!Native.IsWindow(hwnd)) return;
                bool hasLayered = (ex & Native.WS_EX_LAYERED) != 0;

                if (alpha >= 255)
                {
                    if (hasLayered)
                    {
                        if (!Native.IsWindow(hwnd)) return;
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED));
                        // MSDN: 移除 WS_EX_LAYERED 后用 RedrawWindow 而非 InvalidateRect
                        Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                    }
                }
                else
                {
                    if (!hasLayered)
                    {
                        if (!Native.IsWindow(hwnd)) return;
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_LAYERED));
                    }
                    if (!Native.IsWindow(hwnd)) return;
                    Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
                    Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"SetTargetAlpha failed for 0x{hwnd:X}: {ex.Message}"); }
        }

        /// <summary>触发刷新——走 OnUpdate 完整路径（前景蒙版/后台透明）。</summary>
        private void ApplyMaskNow(TargetEntry e)
        {
            e.Tracker.RefreshForeground();
        }

        private void TryBindTarget(TargetInfo info)
        {
            var boundHandles = new HashSet<IntPtr>(_entries.Select(e => e.Tracker.TargetHandle));
            var h = TargetTracker.FindByTitleAndProcess(info.WindowTitle, info.ProcessName, boundHandles);
            if (h == IntPtr.Zero) return;

            // 已绑定到同一个 handle → 跳过
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return;

            // 有该目标的旧条目（窗口关闭后 reopen）→ 更新 handle 复用
            var stale = _entries.FirstOrDefault(e => e.Info == info && !Native.IsWindow(e.Tracker.TargetHandle));
            if (stale != null)
            {
                stale.Tracker.TargetHandle = h;
                stale.Tracker.RefreshNow();
                return;
            }

            // 新绑定
            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            _entries.Add(entry);

            AddTargetUI(entry);
        }

        private void AddTargetUI(TargetEntry entry)
        {
            int w = _pnlTargets.ClientSize.Width - 6;
            var pnl = new Panel { Size = new Size(w, 32), Margin = new Padding(0, 0, 0, 3),
                BackColor = Color.FromArgb(40, 40, 40) };

            var lbl = new Label
            {
                Text = $"  {entry.Info}", AutoSize = true,
                Location = new Point(4, 8), MaximumSize = new Size(300, 20),
                ForeColor = Color.FromArgb(224, 224, 224)
            };
            pnl.Controls.Add(lbl);

            var btnRemove = new Button
            {
                Text = "×", Size = new Size(28, 24),
                Location = new Point(w - 34, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.FromArgb(224, 224, 224)
            };
            btnRemove.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btnRemove.Click += (_, _) => RemoveEntry(entry, pnl);
            pnl.Controls.Add(btnRemove);

            entry.UIPanel = pnl;
            _pnlTargets.Controls.Add(pnl);
            // 将新面板排在倒数第二（在最后添加的控件之前）
            if (_pnlTargets.Controls.Count >= 2)
                _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        /// <summary>为尚未启动的目标窗口创建灰色"待激活"面板。</summary>
        private void AddPendingUI(TargetInfo info)
        {
            if (_pendingPanels.ContainsKey(info)) return;
            int w = _pnlTargets.ClientSize.Width - 6;
            var pnl = new Panel { Size = new Size(w, 32), Margin = new Padding(0, 0, 0, 3),
                BackColor = Color.FromArgb(40, 40, 40) };

            var lbl = new Label
            {
                Text = $"  ⏳ 待激活 — {info}",
                AutoSize = true, Location = new Point(4, 8),
                MaximumSize = new Size(280, 20),
                ForeColor = Color.FromArgb(120, 120, 120)
            };
            pnl.Controls.Add(lbl);

            var btnRemove = new Button
            {
                Text = "×", Size = new Size(28, 24),
                Location = new Point(w - 34, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.FromArgb(224, 224, 224)
            };
            btnRemove.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btnRemove.Click += (_, _) => RemovePendingTarget(info, pnl);
            pnl.Controls.Add(btnRemove);

            _pendingPanels[info] = pnl;
            _pnlTargets.Controls.Add(pnl);
            if (_pnlTargets.Controls.Count >= 2)
                _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        private void RemovePendingUI(TargetInfo info)
        {
            if (_pendingPanels.TryGetValue(info, out var pnl))
            {
                _pnlTargets.Controls.Remove(pnl);
                _pendingPanels.Remove(info);
            }
        }

        private void RemovePendingTarget(TargetInfo info, Panel pnl)
        {
            RemovePendingUI(info);
            // 也清理可能已绑定的条目
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null) RemoveEntry(entry, entry.UIPanel);
            _settings.Targets.Remove(info);
            _settings.Save();
            UpdateUI();
        }

        private void RemoveEntry(TargetEntry entry, Panel pnl)
        {
            _entries.Remove(entry);
            _pnlTargets.Controls.Remove(pnl);
            SetTargetAlpha(entry.Tracker.TargetHandle, 255);
            entry.Tracker.Dispose();
            entry.MaskDark.Dispose();
            _settings.Targets.Remove(entry.Info);
            _settings.Save();
            UpdateUI();
        }

        private void UnbindAll()
        {
            foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.MaskDark.HideMask(); e.Tracker.Dispose(); e.MaskDark.Dispose(); }
            _entries.Clear();
            _pnlTargets.Controls.Clear();
            _pendingPanels.Clear();
        }

        // ════════════════════════════════════════════════════════════
        // 设置界面构建
        // ════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            int y = 10, pad = 10;
            const int GW = 434; // group box width

            AddGroup("状态", pad, ref y, 50, GW, gb =>
            {
                _lblStatus = AddLabel(gb, pad, 20, FontStyle.Bold, 10f);
            });

            _chkEnabled = AddCheck(this, "启用覆盖", pad + 4, y + 2, FontStyle.Bold, _settings.Enabled, ToggleEnabled);
            _chkKeepTransparency = AddCheck(this, "窗口保持透明度（前台也直接用透明度，不叠加蒙版）", 140, y + 2, FontStyle.Regular,
                _settings.KeepTransparency, () => { _settings.KeepTransparency = _chkKeepTransparency.Checked; _settings.Save(); foreach (var e in _entries) e.Tracker.RefreshForeground(); });
            y += 28;

            AddGroup("目标窗口", pad, ref y, 260, GW, gb =>
            {
                _pnlTargets = new FlowLayoutPanel
                {
                    Location = new Point(pad, 18), Size = new Size(416, 210),
                    AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    BackColor = Color.FromArgb(32, 32, 32), ForeColor = Color.White
                };
                _pnlTargets.HandleCreated += (_, _) =>
                    Native.SetWindowTheme(_pnlTargets.Handle, "DarkMode_Explorer", null);
                gb.Controls.Add(_pnlTargets);
            });

            AddButton(this, "+ 添加窗口", pad, y + 2, 95, PickWindow);
            _btnRefind = AddButton(this, "🔄 重新查找", 110, y + 2, 95, RefindAllWindows);
            y += 38;

            AddGroup("透明度", pad, ref y, 90, GW, gb =>
            {
                gb.Controls.Add(new Label { Text = "蒙版 (前台):", Location = new Point(pad, 18), AutoSize = true });
                _tbAlpha = new JumpTrackBar
                {
                    Location = new Point(90, 16), Size = new Size(280, 40),
                    Minimum = 0, Maximum = 100, TickFrequency = 10,
                    SmallChange = 5, LargeChange = 20,
                    Value = _settings.Alpha
                };
                _tbAlpha.ValueChanged += (_, _) => SetAlpha(_tbAlpha.Value);
                _tbAlpha.MouseDown += (_, _) => _previewMask = true;
                _tbAlpha.MouseUp += (_, _) => ClearPreview();
                MouseUp += (_, _) => ClearPreview(); // 兜底：滑块外释放
                gb.Controls.Add(_tbAlpha);
                _lblAlpha = new Label { Location = new Point(376, 24), AutoSize = true };
                gb.Controls.Add(_lblAlpha);

                gb.Controls.Add(new Label { Text = "窗口 (后台):", Location = new Point(pad, 56), AutoSize = true });
                _tbBgAlpha = new JumpTrackBar
                {
                    Location = new Point(90, 54), Size = new Size(280, 40),
                    Minimum = 0, Maximum = 100, TickFrequency = 10,
                    SmallChange = 5, LargeChange = 20,
                    Value = _settings.BackgroundAlpha
                };
                _tbBgAlpha.ValueChanged += (_, _) => { _settings.BackgroundAlpha = Math.Clamp(_tbBgAlpha.Value, 0, 100); _settings.Save(); foreach (var e in _entries) e.Tracker.RefreshForeground(); _lblBgAlpha.Text = $"{_tbBgAlpha.Value}%"; };
                gb.Controls.Add(_tbBgAlpha);
                _lblBgAlpha = new Label { Location = new Point(376, 62), AutoSize = true };
                gb.Controls.Add(_lblBgAlpha);
            });

            // 系统选项
            int rowY = y + 2;
            _chkStartup = AddCheck(this, "开机自启", pad + 4, rowY, FontStyle.Regular, _settings.StartWithWindows,
                () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); _settings.Save(); });
            _chkMinimizeTray = AddCheck(this, "关闭窗口时最小化到托盘（不勾选则直接退出）", pad + 4, rowY + 24, FontStyle.Regular,
                _settings.MinimizeToTray, () => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; _settings.Save(); });
            rowY += 52;

            AddButton(this, "📂 配置文件夹", pad, rowY, 120, OpenConfigFolder);
            AddButton(this, "ℹ 关于", 134, rowY, 70, ShowAbout);
            AddButton(this, "💾 保存配置", 210, rowY, 110, SaveSettings);
            AddButton(this, "🚪 退出", 326, rowY, 70, () => { _reallyQuit = true; Close(); });

            var link = new LinkLabel
            {
                Text = "20260714 / 世界的风吹向你 / 开源软件",
                Location = new Point(pad, rowY + 38),
                AutoSize = true,
                LinkColor = Color.FromArgb(160, 160, 170),
                ActiveLinkColor = Color.FromArgb(200, 200, 220)
            };
            link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Simiely/WindowTinter") { UseShellExecute = true });
            Controls.Add(link);

            // 恢复已有条目（活跃 + 待激活）
            for (int i = 0; i < _entries.Count; i++)
                AddTargetUI(_entries[i]);
            foreach (var t in _settings.Targets)
                if (!_entries.Any(e => e.Info == t))
                    AddPendingUI(t);

            ApplyDarkTheme();
        }

        // ── 控件辅助方法 ──────────────────────────────────────────

        private void AddGroup(string text, int x, ref int y, int h, int w, Action<GroupBox> build)
        {
            var gb = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            // WinForms GroupBox 内部区域由系统主题绘制，BackColor 无效，需自绘填充
            var groupBg = Color.FromArgb(40, 40, 40);
            gb.Paint += (_, e) =>
            {
                using var brush = new SolidBrush(groupBg);
                // 仅填充内部（跳过标题栏和边框区域）
                e.Graphics.FillRectangle(brush, 3, 16, w - 6, h - 19);
            };
            build(gb);
            Controls.Add(gb);
            y += h + 6;
        }

        private static Label AddLabel(Control parent, int x, int y, FontStyle style, float size = 9f)
        {
            var lbl = new Label { Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", size, style) };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Button AddButton(Control parent, string text, int x, int y, int w, Action onClick)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30) };
            btn.Click += (_, _) => onClick();
            parent.Controls.Add(btn);
            return btn;
        }

        private static CheckBox AddCheck(Control parent, string text, int x, int y, FontStyle style, bool initial, Action onChange)
        {
            var chk = new CheckBox
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Checked = initial
            };
            if (style != FontStyle.Regular)
                chk.Font = new Font("Microsoft YaHei UI", 9.5f, style);
            chk.CheckedChanged += (_, _) => onChange();
            parent.Controls.Add(chk);
            return chk;
        }

        // ════════════════════════════════════════════════════════════
        // 深色主题
        // ════════════════════════════════════════════════════════════

        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.FromArgb(224, 224, 224);
            var panelBg = Color.FromArgb(40, 40, 40);

            BackColor = bg;
            ForeColor = fg;
            if (IsHandleCreated)
            {
                int dark = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            }
            ThemeAll(this, bg, fg, panelBg);
        }

        private static void ThemeAll(Control parent, Color bg, Color fg, Color panelBg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is GroupBox or Panel or FlowLayoutPanel) { c.BackColor = panelBg; c.ForeColor = fg; }
                else if (c is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = fg; btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (c is CheckBox or RadioButton) { c.BackColor = bg; c.ForeColor = fg; }
                else if (c is Label lbl) { lbl.BackColor = lbl.Parent is Panel or FlowLayoutPanel ? panelBg : bg; if (lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.FromArgb(140, 140, 140); else lbl.ForeColor = fg; }
                else if (c is TrackBar) { c.BackColor = panelBg; }
                else { c.BackColor = bg; c.ForeColor = fg; }
                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        // ════════════════════════════════════════════════════════════
        // UI 同步
        // ════════════════════════════════════════════════════════════

        private void UpdateUI()
        {
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            _lblStatus.Text = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 个监控中, {pending} 个待激活"
                : $"● 监控中 — {active} 个窗口";

            _chkEnabled.Checked = _settings.Enabled;

            int sv = _settings.Alpha;
            if (_tbAlpha.Value != sv) _tbAlpha.Value = sv;
            _lblAlpha.Text = $"{_tbAlpha.Value}%";
            if (_tbBgAlpha.Value != _settings.BackgroundAlpha) _tbBgAlpha.Value = _settings.BackgroundAlpha;
            _lblBgAlpha.Text = $"{_settings.BackgroundAlpha}%";

            _chkStartup.Checked = _settings.StartWithWindows;
            _chkKeepTransparency.Checked = _settings.KeepTransparency;
            _chkMinimizeTray.Checked = _settings.MinimizeToTray;
            RefreshTrayMenu();
        }

        // ════════════════════════════════════════════════════════════
        // 托盘
        // ════════════════════════════════════════════════════════════

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = _appIcon, Text = "暗幕 v3.6.2", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            string s = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 监控中, {pending} 待激活"
                : $"● 监控中 — {active} 窗口";
            _menu.Items.Add(s).Enabled = false;
            _menu.Items.Add("-");
            _menu.Items.Add(Visible ? "最小化到托盘" : "打开设置窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => _chkEnabled.Checked = !_chkEnabled.Checked);
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (_, _) => { _reallyQuit = true; Close(); });
        }

        private void ToggleWindow()
        {
            if (Visible) Hide();
            else { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            { _settings.Save(); e.Cancel = true; Hide(); }
        }

        // ════════════════════════════════════════════════════════════
        // 核心操作
        // ════════════════════════════════════════════════════════════

        private void ToggleEnabled()
        {
            // 直接读 checkbox 状态，避免 _settings.Enabled 翻转与 UI 不一致
            bool enable = _chkEnabled.Checked;
            _settings.Enabled = enable;
            _settings.Save();
            if (enable)
            {
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    if (_entries.Any(e => e.Info == t)) RemovePendingUI(t);
                    else if (!_pendingPanels.ContainsKey(t)) AddPendingUI(t);
                }
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            else
            {
                foreach (var e in _entries) { e.MaskDark.HideMask(); SetTargetAlpha(e.Tracker.TargetHandle, 255); }
            }
            UpdateUI();
        }

        private void SetAlpha(int value)
        {
            _settings.Alpha = Math.Clamp(value, 0, 100);
            _settings.Save();
            foreach (var e in _entries) ApplyMaskNow(e);
            _lblAlpha.Text = $"{_settings.Alpha}%";
        }

        private void ClearPreview()
        {
            if (!_previewMask) return;
            _previewMask = false;
            foreach (var e in _entries) e.Tracker.RefreshForeground();
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            var info = new TargetInfo();
            try { info.ProcessName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                info.WindowTitle = sb.ToString();
            }

            if (_settings.Targets.Contains(info))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);

            if (_settings.Enabled)
            {
                TryBindTarget(info);
                var entry = _entries.FirstOrDefault(e => e.Info == info);
                if (entry != null) ApplyMaskNow(entry);
                else AddPendingUI(info);
            }
            else AddPendingUI(info);
            UpdateUI();
        }

        private void RefindAllWindows()
        {
            UnbindAll();
            foreach (var t in _settings.Targets)
            {
                TryBindTarget(t);
                if (!_entries.Any(e => e.Info == t)) AddPendingUI(t);
            }
            UpdateUI();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "暗幕 v3.6.2\n\n" +
                "给任意窗口叠加深色半透明蒙版的常驻小工具。\n" +
                "支持多窗口同时覆盖。\n\n" +
                "• 配置: 与 exe 同目录 WindowTinter.settings.json\n" +
                "• 图标: app.ico 与 exe 同目录\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }

        // ════════════════════════════════════════════════════════════
        // WinEvent
        // ════════════════════════════════════════════════════════════

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

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshForeground(); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            // 目标特定事件：在 BeginInvoke 内读取 _entries，避免跨线程访问非安全集合
            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                try { BeginInvoke(new Action(() =>
                {
                    var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                    if (match != null) match.Tracker.RefreshNow();
                })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }

        private void SaveSettings()
        {
            _settings.Save();
        }

        private void Quit()
        {
            _settings.Save();
            _reallyQuit = true;
            _autoBindTimer?.Stop(); _autoBindTimer?.Dispose();
            _onShownTimer?.Stop(); _onShownTimer?.Dispose();
            _tray.Visible = false;  // 先隐藏托盘（内部会访问 Icon.Handle）
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.MaskDark.HideMask(); e.MaskDark.Dispose(); e.Tracker.Dispose(); }
            _pendingPanels.Clear();
            _appIcon?.Dispose();  // 最后释放——NotifyIcon 已不再引用它
        }

        // ════════════════════════════════════════════════════════════
        // 入口
        // ════════════════════════════════════════════════════════════

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
        }
    }
}
