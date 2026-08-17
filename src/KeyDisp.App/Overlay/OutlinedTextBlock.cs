using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace KeyDisp.App.Overlay;

/// <summary>テキストの 1 区間 (×n の縮小・強調などフォント指定が変わる単位)。</summary>
public readonly record struct TextSegment(
    string Text, double FontSize, FontWeight Weight, double BaselineShift = 0);

/// <summary>
/// 縁取り付きテキスト。FormattedText の Geometry を背面に Stroke、前面に Fill で 2 回描く。
/// Mac 版の「影 8 方向」ハックより真の輪郭になる。縁取りオフのときは DrawText のみ。
/// MaxTextWidth で折り返す (シンプル/カスタム画像スタイルの行本文用)。
/// </summary>
public sealed class OutlinedTextBlock : FrameworkElement
{
    private IReadOnlyList<TextSegment> _segments = Array.Empty<TextSegment>();
    private FormattedText? _formatted;

    public Brush Fill { get; set; } = Brushes.White;
    public Brush? OutlineBrush { get; set; }
    public double OutlineWidth { get; set; }
    public double MaxTextWidth { get; set; } = double.PositiveInfinity;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public IReadOnlyList<TextSegment> Segments
    {
        get => _segments;
        set
        {
            _segments = value;
            _formatted = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private FormattedText BuildText()
    {
        if (_formatted is not null) return _formatted;
        var full = string.Concat(_segments.Select(s => s.Text));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var baseSize = _segments.Count > 0 ? _segments[0].FontSize : 34;
        var text = new FormattedText(
            full, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(OverlayConstants.FontFamilyName),
                FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            baseSize, Fill, dpi)
        {
            TextAlignment = Alignment,
        };
        if (!double.IsInfinity(MaxTextWidth) && MaxTextWidth > 0)
        {
            text.MaxTextWidth = MaxTextWidth;
        }
        var index = 0;
        foreach (var segment in _segments)
        {
            var length = segment.Text.Length;
            if (length > 0)
            {
                text.SetFontSize(segment.FontSize, index, length);
                text.SetFontWeight(segment.Weight, index, length);
                if (segment.BaselineShift != 0)
                {
                    // 光学調整 (↩ など): 該当区間だけベースラインを下げる代わりに
                    // FormattedText では表現できないため、描画時の全体オフセットはせず無視する。
                    // 行内の 1 文字の光学調整はキーキャップ側でのみ行う。
                }
            }
            index += length;
        }
        _formatted = text;
        return text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_segments.Count == 0) return new Size(0, 0);
        var text = BuildText();
        return new Size(
            Math.Min(text.WidthIncludingTrailingWhitespace, availableSize.Width),
            text.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_segments.Count == 0) return;
        var text = BuildText();
        var origin = new Point(0, 0);
        if (OutlineBrush is not null && OutlineWidth > 0)
        {
            var geometry = text.BuildGeometry(origin);
            var pen = new Pen(OutlineBrush, OutlineWidth * 2)
            {
                LineJoin = PenLineJoin.Round,
            };
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.DrawText(text, origin);
    }
}
