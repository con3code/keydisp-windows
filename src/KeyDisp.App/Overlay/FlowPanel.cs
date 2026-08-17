using System.Windows;
using System.Windows.Controls;

namespace KeyDisp.App.Overlay;

/// <summary>
/// 幅に収まらない要素を次の行へ折り返すパネル (Mac 版 FlowLayout の移植)。
/// 行の分け方は MakeLines だけが決め、計測と配置で必ず同じ幅 (計測時の提案幅) を使う。
/// 配置時の finalSize はピクセル丸めでわずかに狭いことがあり、判定が食い違うと
/// 行頭にキーが重なって見える (Mac 版 DEVLOG の教訓)。行内は縦センタリング。
/// </summary>
public sealed class FlowPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(FlowPanel),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>計測時に使った提案幅。配置でも同じ値で行分けする。</summary>
    private double _measureWidth = double.PositiveInfinity;

    private List<List<UIElement>> MakeLines(double maxWidth)
    {
        var lines = new List<List<UIElement>>();
        var line = new List<UIElement>();
        double lineWidth = 0;
        foreach (UIElement child in InternalChildren)
        {
            var w = child.DesiredSize.Width;
            if (line.Count > 0 && lineWidth + Spacing + w > maxWidth)
            {
                lines.Add(line);
                line = new List<UIElement>();
                lineWidth = 0;
            }
            lineWidth += line.Count == 0 ? w : Spacing + w;
            line.Add(child);
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        _measureWidth = availableSize.Width;
        double width = 0, height = 0;
        var lines = MakeLines(_measureWidth);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineWidth = line.Sum(c => c.DesiredSize.Width) + Spacing * (line.Count - 1);
            width = Math.Max(width, lineWidth);
            height += (i > 0 ? Spacing : 0) + line.Max(c => c.DesiredSize.Height);
        }
        if (double.IsInfinity(width)) width = 0;
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var line in MakeLines(_measureWidth))
        {
            var rowHeight = line.Max(c => c.DesiredSize.Height);
            double x = 0;
            foreach (var child in line)
            {
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y + (rowHeight - size.Height) / 2, size.Width, size.Height));
                x += size.Width + Spacing;
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
