using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WindowTinter.ViewModels
{
    /// <summary>阶段 B 临时卡片数据（渲染设计稿 1:1 截图用；阶段 C 换成真实 TargetViewModel）。</summary>
    public class TempCard
    {
        public string DisplayName { get; set; }
        public Visibility AliasVisible { get; set; }
        public Brush ShotColor { get; set; }
        public Visibility PendingLabel { get; set; }
        public Brush CardBg { get; set; }
        public Brush CardBorder { get; set; }
        public Brush StateColor { get; set; }
        public Brush NameColor { get; set; }
        public string SelText { get; set; }
        public Brush SelBg { get; set; }
    }

    /// <summary>阶段 B 假数据工厂。</summary>
    public static class FakeData
    {
        public static List<TempCard> Create()
        {
            var list = new List<TempCard>();
            list.Add(Card("我的测试窗口", true, "#4A90D9", false, "#2B3B52", "#4A90D9", true, "●"));
            list.Add(Card("Chrome - 哔哩哔哩", false, "#1D9E75", false, "#32363C", "#3A3F46", false, "○"));
            list.Add(Card("无标题 - 记事本", false, "#D9A44A", false, "#32363C", "#3A3F46", false, "○"));
            list.Add(Card("Visual Studio Code", false, "#E5534B", false, "#32363C", "#3A3F46", false, "○"));
            list.Add(Card("微信", false, "#7F77DD", false, "#32363C", "#3A3F46", false, "○"));
            list.Add(Card("目标B", false, "#000000", true, "#26292E", "#555", false, "○"));
            return list;
        }

        private static TempCard Card(string name, bool alias, string shot, bool pending, string bg, string border, bool sel, string selText)
        {
            return new TempCard
            {
                DisplayName = name,
                AliasVisible = alias ? Visibility.Visible : Visibility.Collapsed,
                ShotColor = Br(h(shot)),
                PendingLabel = pending ? Visibility.Visible : Visibility.Collapsed,
                CardBg = Br(h(bg)),
                CardBorder = Br(h(border)),
                StateColor = pending ? Br(h("#6E747C")) : Br(h("#3ECF8E")),
                NameColor = pending ? Br(h("#6E747C")) : Br(h("#E8EAF0")),
                SelText = selText,
                SelBg = sel ? Br(h("#4A90D9")) : Br(h("#3A3F46"))
            };
        }

        private static Color h(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        private static SolidColorBrush Br(Color c) => new SolidColorBrush(c);
    }
}
