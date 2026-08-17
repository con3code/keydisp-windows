using System.Windows;
using System.Windows.Media;
using KeyDisp.Core.Display;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Overlay;

/// <summary>オーバーレイの 1 行ぶんの表示値 (MVP: シンプルスタイル)。</summary>
public sealed class OverlayRowVm
{
    public OverlayRowVm(KeyEntry entry, AppSettings settings, double windowWidth)
    {
        var scale = settings.DisplayScale;
        var text = string.Concat(entry.Tokens);
        DisplayText = entry.Count > 1 ? $"{text} ×{entry.Count}" : text;
        FontSize = 34 * scale;
        CornerRadius = new CornerRadius(14 * scale);
        RowPadding = new Thickness(14 * scale, 7 * scale, 14 * scale, 7 * scale);
        RowMargin = new Thickness(0, 4 * scale, 0, 4 * scale);
        MaxTextWidth = Math.Max(50, windowWidth - 32 - 28 * scale);
        Foreground = new SolidColorBrush(ParseColor(settings.TextColorHex, Colors.White));
        var bg = ParseColor(settings.KeyColorHex, Color.FromRgb(0x1C, 0x1C, 0x22));
        var opacity = settings.BackgroundEnabled ? settings.BackgroundOpacity : 0;
        bg.A = (byte)Math.Clamp(opacity * 255, 0, 255);
        Background = new SolidColorBrush(bg);
        RowAlignment = settings.RowAlignment switch
        {
            Core.Settings.RowAlignment.Center => HorizontalAlignment.Center,
            Core.Settings.RowAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
    }

    public string DisplayText { get; }
    public double FontSize { get; }
    public CornerRadius CornerRadius { get; }
    public Thickness RowPadding { get; }
    public Thickness RowMargin { get; }
    public double MaxTextWidth { get; }
    public Brush Foreground { get; }
    public Brush Background { get; }
    public HorizontalAlignment RowAlignment { get; }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }
}
