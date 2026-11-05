using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WindowTinter.Views
{
    /// <summary>
    /// 卡片面板：1 列 = 逐张整行；2 列 = 双列排布，但「选中的卡片」自动占满整行。
    /// 之所以让选中卡占满整行：双列时卡片内宽约 275px，内联滑块只剩约 130px 行程，
    /// 手感不够；展开态改为整行后行程回到单列水平，其余卡片仍是双列。
    /// </summary>
    internal class CardsPanel : Panel
    {
        private const double GapX = 16;   // 双列间距
        private const double GapY = 8;    // 行间距

        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(CardsPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>当前选中的卡片（TargetViewModel），该卡片测量/排布时占满整行。</summary>
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem), typeof(object), typeof(CardsPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private struct Slot
        {
            public UIElement Child;
            public int Row;
            public double X;
            public double Width;
        }

        /// <summary>按可用宽度计算每个子元素的落位；宽卡（选中卡）单独起一行并占满整行。</summary>
        private List<Slot> BuildSlots(double width)
        {
            int cols = Columns >= 2 ? 2 : 1;
            double cellW = cols == 1 ? width : Math.Max(0, (width - GapX) / 2);

            var slots = new List<Slot>(InternalChildren.Count);
            int row = 0, col = 0;
            foreach (UIElement child in InternalChildren)
            {
                bool wide = cols > 1 && child is FrameworkElement fe && fe.DataContext != null
                            && ReferenceEquals(fe.DataContext, SelectedItem);
                if (cols == 1 || wide)
                {
                    if (wide && col > 0) { row++; col = 0; } // 半行未满：展开卡另起一行
                    slots.Add(new Slot { Child = child, Row = row, X = 0, Width = width });
                    row++;
                    col = 0;
                }
                else
                {
                    slots.Add(new Slot
                    {
                        Child = child,
                        Row = row,
                        X = col == 0 ? 0 : cellW + GapX,
                        Width = cellW
                    });
                    col++;
                    if (col >= cols) { row++; col = 0; }
                }
            }
            return slots;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
                ? 640 : availableSize.Width;

            var slots = BuildSlots(width);
            var rowHeights = new Dictionary<int, double>();
            foreach (var s in slots)
            {
                s.Child.Measure(new Size(s.Width, double.PositiveInfinity));
                double h = s.Child.DesiredSize.Height;
                rowHeights[s.Row] = rowHeights.TryGetValue(s.Row, out double prev) ? Math.Max(prev, h) : h;
            }

            double total = 0;
            foreach (var kv in rowHeights) total += kv.Value + GapY;
            if (rowHeights.Count > 0) total -= GapY;

            return new Size(width, total);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var slots = BuildSlots(finalSize.Width);
            var rowTops = new Dictionary<int, double>();
            double y = 0;
            var maxRow = -1;
            foreach (var s in slots) if (s.Row > maxRow) maxRow = s.Row;

            // 行高 = 该行最高的子元素（展开卡更高时整行跟随）
            for (int r = 0; r <= maxRow; r++)
            {
                double h = 0;
                foreach (var s in slots)
                    if (s.Row == r) h = Math.Max(h, s.Child.DesiredSize.Height);
                rowTops[r] = y;
                y += h + GapY;
            }

            foreach (var s in slots)
            {
                double h = s.Child.DesiredSize.Height;
                s.Child.Arrange(new Rect(s.X, rowTops[s.Row], s.Width, h));
            }

            return finalSize;
        }
    }
}
