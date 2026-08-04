using System;
using DrawingBitmap = System.Drawing.Bitmap;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowTinter.ViewModels
{
    /// <summary>
    /// 单目标卡片 VM（设计稿扑克牌卡片）。
    /// 数据源：TargetInfo（配置）+ TargetEntry（活跃绑定条目，null=待激活）。
    /// 外观色板对齐 Themes/Dark.xaml 与 UI_REDESIGN.html；选中态由 MainViewModel.SelectedTarget 驱动。
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
            RenameCommand = new RelayCommand(() => _owner.RenameTarget(this));
            RemoveCommand = new RelayCommand(() => _owner.RemoveTarget(this));
            RefreshShotCommand = new RelayCommand(() => _owner.RefreshTargetSnapshot(Info));
        }

        // ── 外观色板（对齐 Themes/Dark.xaml）──
        private static readonly Brush C_Card = Br("#32363C");
        private static readonly Brush C_CardPending = Br("#26292E");
        private static readonly Brush C_CardSel = Br("#2B3B52");
        private static readonly Brush C_Border = Br("#3A3F46");
        private static readonly Brush C_BorderPending = Br("#555555");
        private static readonly Brush C_BorderSel = Br("#4A90D9");
        private static readonly Brush C_Teal = Br("#3ECF8E");
        private static readonly Brush C_Dim = Br("#6E747C");
        private static readonly Brush C_Text = Br("#E8EAF0");
        private static readonly Brush C_Btn = Br("#3A3F46");
        private static readonly Brush C_BtnSel = Br("#4A90D9");
        private static readonly Brush C_Shot = Br("#2A2E36");

        // ── 状态派生 ──
        public bool IsPending => Entry == null;
        public bool IsSelected => _owner.SelectedTarget == this;

        public string DisplayName => Info.ToString();
        public Visibility AliasVisible => string.IsNullOrEmpty(Info.Alias) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PendingLabel => IsPending ? Visibility.Visible : Visibility.Collapsed;

        public Brush ShotColor => C_Shot;
        public Brush StateColor => IsPending ? C_Dim : C_Teal;
        public Brush NameColor => IsPending ? C_Dim : C_Text;
        public Brush CardBg => IsSelected && !IsPending ? C_CardSel : IsPending ? C_CardPending : C_Card;
        public Brush CardBorder => IsSelected && !IsPending ? C_BorderSel : IsPending ? C_BorderPending : C_Border;

        public bool SelectEnabled => !IsPending && !_owner.GlobalTransparency;
        public string SelText => IsSelected ? "●" : "○";
        public Brush SelBg => IsSelected ? C_BtnSel : C_Btn;

        // ── 快照（BitmapSource；null = 显示占位色块）──
        private ImageSource _shotImage;
        public ImageSource ShotImage { get => _shotImage; private set => SetProperty(ref _shotImage, value); }

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

        /// <summary>绑定状态变化 / 选中态变化时刷新卡片外观。由 MainViewModel 统一驱动。</summary>
        public void RefreshCardVisuals()
        {
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AliasVisible));
            OnPropertyChanged(nameof(PendingLabel));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(NameColor));
            OnPropertyChanged(nameof(CardBg));
            OnPropertyChanged(nameof(CardBorder));
            OnPropertyChanged(nameof(SelectEnabled));
            OnPropertyChanged(nameof(SelText));
            OnPropertyChanged(nameof(SelBg));
        }

        /// <summary>由 RebuildTargetList 调用：绑定条目变化（活跃↔待激活）。</summary>
        public void UpdateEntry(TargetEntry entry)
        {
            Entry = entry;
            RefreshCardVisuals();
        }

        // ── 卡片命令 ──
        public RelayCommand SelectCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand RefreshShotCommand { get; }

        private static Brush Br(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
    }
}
