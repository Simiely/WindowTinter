using System;
using DrawingBitmap = System.Drawing.Bitmap;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowTinter.ViewModels
{
    /// <summary>
    /// 单目标卡片 VM。
    /// 只暴露语义状态（生效 / 未找到 / 暂停 / 刷新中），不产出颜色——颜色由 XAML 按 StateTone 触发选择。
    /// </summary>
    internal class TargetViewModel : ObservableObject
    {
        private readonly MainViewModel _owner;

        public TargetInfo Info { get; }
        private TargetEntry _entry;
        public TargetEntry Entry
        {
            get => _entry;
            private set => SetProperty(ref _entry, value);
        }

        public TargetViewModel(MainViewModel owner, TargetInfo info)
        {
            _owner = owner;
            Info = info;
            SelectCommand = new RelayCommand(() => _owner.SelectTarget(this));
            ExpandCommand = new RelayCommand(() => _owner.ExpandTarget(this));
            CaretCommand = new RelayCommand(() => _owner.ToggleExpandedFromCaret(this));
            RenameCommand = new RelayCommand(() => _owner.RenameTarget(this));
            RemoveCommand = new RelayCommand(() => _owner.RemoveTarget(this));
            RefreshShotCommand = new RelayCommand(() => _owner.RefreshTargetSnapshot(Info));
        }

        // ── 状态派生（XAML 据 StateTone 选色）──
        public bool IsPending => Entry == null;
        public bool IsSelected => _owner.SelectedTarget == this;

        /// <summary>单卡启用开关（双向绑定卡片右侧的 32×20 开关）。与全局总闸分层。</summary>
        public bool IsEnabled
        {
            get => Info.Enabled;
            set => _owner.SetTargetEnabled(this, value);
        }

        public bool IsDisabled => !Info.Enabled;
        public Visibility DisabledVisible => Info.Enabled ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>ok = 已绑定生效；warn = 窗口未找到；off = 全局暂停或本卡已停用。</summary>
        public string StateTone => !_owner.IsEnabled || !Info.Enabled ? "off" : IsPending ? "warn" : "ok";

        // ── 文案：标题优先级 = 窗口标题 > 进程名；别名单独放副标题 ──
        public string DisplayName => Info.ToString();
        public string WindowName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Info.WindowTitle)) return Info.WindowTitle;
                if (!string.IsNullOrWhiteSpace(Info.ProcessName)) return Info.ProcessName;
                return "未命名窗口";
            }
        }
        public string AliasText => Info.Alias ?? "";
        public bool HasAlias => !string.IsNullOrEmpty(Info.Alias);
        public Visibility AliasVisible => HasAlias ? Visibility.Visible : Visibility.Collapsed;

        // ── 副行：暗度 / 圆角（取该目标生效值，文本在 VM 内拼装，不涉及颜色）──
        public string AlphaText => $"暗度 {_owner.EffectiveAlpha(Info)}%";
        public string CornerText => _owner.EffectiveCorner(Info) == 0 ? "直角" : $"圆角 {_owner.EffectiveCorner(Info)}px";
        public string MetaSuffix => $"{AlphaText} · {CornerText}";
        /// <summary>副行整行文本（窄卡片下由 TextBlock 单行省略，避免横向溢出）。</summary>
        public string MetaLine => HasAlias ? $"{AliasText} · {MetaSuffix}" : MetaSuffix;

        // ── 快照（BitmapSource；null = 显示占位）──
        private ImageSource _shotImage;
        public ImageSource ShotImage { get => _shotImage; private set { if (SetProperty(ref _shotImage, value)) OnPropertyChanged(nameof(HasShot)); } }

        public bool HasShot => _shotImage != null;
        public Visibility ShotVisible => HasShot ? Visibility.Visible : Visibility.Collapsed;

        private bool _shotFailed;
        /// <summary>最近一次抓图失败（窗口最小化 / 受保护 / 全黑）。有旧帧时仍保留旧帧。</summary>
        public bool ShotFailed => _shotFailed;

        public string PlaceholderText => _shotFailed ? "未能抓取" : "窗口未找到";

        public void SetShotFailed(bool failed)
        {
            if (_shotFailed == failed) return;
            _shotFailed = failed;
            OnPropertyChanged(nameof(ShotFailed));
            OnPropertyChanged(nameof(PlaceholderText));
        }

        private bool _isRefreshing;
        public bool IsRefreshing { get => _isRefreshing; private set => SetProperty(ref _isRefreshing, value); }
        public void SetRefreshing(bool value) => IsRefreshing = value;

        /// <summary>设置快照（消费并释放 System.Drawing.Bitmap）；null = 清空回占位。</summary>
        public void SetShot(DrawingBitmap bmp)
        {
            if (bmp == null) { ShotImage = null; return; }
            try
            {
                IntPtr hbit = bmp.GetHbitmap();
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(
                        hbit, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    ShotImage = src;
                }
                finally { Native.DeleteObject(hbit); }
            }
            catch { ShotImage = null; }
            finally { bmp.Dispose(); }
        }

        /// <summary>后台抓图路径：直接设置已 Freeze 的 BitmapSource（UI 线程调用）。</summary>
        public void SetShotSource(ImageSource src)
        {
            ShotImage = src;
        }

        /// <summary>绑定状态变化时刷新卡片外观。由 MainViewModel 统一驱动。</summary>
        public void RefreshCardVisuals()
        {
            OnPropertyChanged(nameof(MetaLine));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(IsDisabled));
            OnPropertyChanged(nameof(DisabledVisible));
            OnPropertyChanged(nameof(StateTone));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(WindowName));
            OnPropertyChanged(nameof(AliasText));
            OnPropertyChanged(nameof(HasAlias));
            OnPropertyChanged(nameof(AliasVisible));
            OnPropertyChanged(nameof(ShotVisible));
            OnPropertyChanged(nameof(PlaceholderText));
        }

        /// <summary>暗度/圆角文案变化（全局模式或本目标被编辑时）。</summary>
        public void RefreshMeta()
        {
            OnPropertyChanged(nameof(AlphaText));
            OnPropertyChanged(nameof(CornerText));
            OnPropertyChanged(nameof(MetaSuffix));
        }

        /// <summary>由 RebuildTargetList 调用：绑定条目变化（活跃↔待激活）。</summary>
        public void UpdateEntry(TargetEntry entry)
        {
            Entry = entry;
            RefreshCardVisuals();
        }

        // ── 卡片命令 ──
        public RelayCommand SelectCommand { get; }
        public RelayCommand ExpandCommand { get; }
        public RelayCommand CaretCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand RefreshShotCommand { get; }
    }
}
