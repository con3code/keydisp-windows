using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyDisp.Core.Layout;

namespace KeyDisp.App.Services;

/// <summary>
/// FormattedText によるトークン幅の実測 (Mac 版の NSFont 実測に相当)。
/// フォントは描画と同じ Segoe UI 系を使うこと。キャッシュは OverlayMetrics 側が持つ。
/// </summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    private static readonly Typeface KeycapTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface BodyTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public double MeasureKeycapText(string token) => Measure(token, KeycapTypeface, 30);

    public double MeasureText(string token) => Measure(token, BodyTypeface, 34);

    private static double Measure(string token, Typeface typeface, double size)
    {
        var text = new FormattedText(
            token, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, size, Brushes.White, pixelsPerDip: 1.0);
        return text.WidthIncludingTrailingWhitespace;
    }
}
