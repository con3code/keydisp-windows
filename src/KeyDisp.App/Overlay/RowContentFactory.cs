using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Settings;

namespace KeyDisp.App.Overlay;

/// <summary>
/// 1 行ぶんの表示要素を組み立てる (Mac 版 KeyEntryRow.styledBody の移植)。
/// スタイル: シンプル / キーキャップ / カスタム背景画像。
/// </summary>
internal static class RowContentFactory
{
    /// <summary>クリックトークンの表示 (モノクロ強制のマウス絵文字 + ボタン種の添字)。</summary>
    private static (string Glyph, string? Suffix) ClickGlyph(string token) => token switch
    {
        KeyFormatter.ClickTokenRight => ("\U0001F5B1︎", "R"),
        KeyFormatter.ClickTokenMiddle => ("\U0001F5B1︎", "M"),
        _ => ("\U0001F5B1︎", null),
    };

    /// <param name="animateAppend">
    /// タイピング連結で文字が増えた更新か (typingAnimation オン時のみ)。
    /// キーキャップでは末尾のキーが spring で現れる。連結テキストの 2 スタイルは
    /// 文字がその場に現れる表現のため対象外 (Mac 版でも効果は控えめ)。
    /// </param>
    public static UIElement Build(
        IReadOnlyList<string> tokens, bool isTyping, int count,
        AppSettings settings, KeyFormatter formatter, double maxWidth,
        bool animateAppend = false)
    {
        return settings.KeyStyle switch
        {
            KeyStyle.Keycap => BuildKeycapRow(tokens, isTyping, count, settings, formatter, animateAppend),
            KeyStyle.CustomImage => BuildTextRow(tokens, isTyping, count, settings, formatter, maxWidth,
                custom: true),
            _ => BuildTextRow(tokens, isTyping, count, settings, formatter, maxWidth, custom: false),
        };
    }

    // ── 共通部品 ─────────────────────────────────────────

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

    private static Color TextColor(AppSettings s) => ParseColor(s.TextColorHex, Colors.White);
    private static Color KeyColor(AppSettings s) =>
        ParseColor(s.KeyColorHex, Color.FromRgb(0x1C, 0x1C, 0x22));

    private static Color WithOpacity(Color c, double opacity) =>
        Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), c.R, c.G, c.B);

    private static Color Darken(Color c, double factor) => Color.FromArgb(
        c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    /// <summary>
    /// キーの境目に挟む文字 (Mac 版 tokenSeparator)。
    /// 「+」なしのコンビネーションは thin space (U+2009) で区切り、タイピングは区切らない。
    /// いずれもゼロ幅スペース (U+200B) を添えて折り返し可能にする。
    /// </summary>
    private static string Separator(bool showPlus, bool isTyping) =>
        showPlus ? "+​" : isTyping ? "​" : " ​";

    private static TextAlignment TextAlign(AppSettings s) => s.RowAlignment switch
    {
        RowAlignment.Center => TextAlignment.Center,
        RowAlignment.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    // ── シンプル / カスタム画像 (連結テキスト) ──────────────

    private static UIElement BuildTextRow(
        IReadOnlyList<string> tokens, bool isTyping, int count,
        AppSettings settings, KeyFormatter formatter, double maxWidth, bool custom)
    {
        var scale = settings.DisplayScale;
        var fontSize = OverlayConstants.BodyFontSize * scale;
        var showPlus = settings.PlusSeparator && !isTyping;
        var textColor = TextColor(settings);
        var keyColor = KeyColor(settings);
        var bgOpacity = settings.BackgroundEnabled ? settings.BackgroundOpacity : 0;

        var segments = new List<TextSegment>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) segments.Add(new TextSegment(Separator(showPlus, isTyping), fontSize, FontWeights.SemiBold));
            var token = tokens[i];
            if (KeyFormatter.IsClickToken(token))
            {
                var (glyph, suffix) = ClickGlyph(token);
                segments.Add(new TextSegment(glyph, fontSize, FontWeights.SemiBold));
                if (suffix is not null)
                {
                    segments.Add(new TextSegment(suffix, fontSize * 0.5, FontWeights.Heavy));
                }
            }
            else if (settings.GlobeOnImeKeys && formatter.IsImeSwitchToken(token))
            {
                segments.Add(new TextSegment("\U0001F310︎" + token, fontSize, FontWeights.SemiBold));
            }
            else
            {
                segments.Add(new TextSegment(token, fontSize, FontWeights.SemiBold));
            }
        }
        if (count > 1)
        {
            segments.Add(new TextSegment(
                $" ×{count}", fontSize * OverlayConstants.CountFontScale, FontWeights.Heavy));
        }

        var paddingH = (custom ? OverlayConstants.CustomPaddingH : OverlayConstants.SimplePaddingH) * scale;
        var paddingV = (custom ? OverlayConstants.CustomPaddingV : OverlayConstants.SimplePaddingV) * scale;
        var text = new OutlinedTextBlock
        {
            Fill = new SolidColorBrush(textColor),
            OutlineBrush = settings.TextOutline
                ? new SolidColorBrush(ParseColor(settings.TextOutlineColorHex, Colors.Black))
                : null,
            OutlineWidth = OverlayConstants.OutlineWidth * scale,
            MaxTextWidth = Math.Max(40, maxWidth - paddingH * 2),
            Alignment = TextAlign(settings),
            Segments = segments,
        };

        if (custom)
        {
            var imageBaseHeight = fontSize * 1.2 + 20 * scale;
            return new NinePatchDecorator
            {
                ImagePath = settings.CustomImagePath,
                BaseHeight = imageBaseHeight,
                // Mac 版準拠: 画像があるときは「背景を表示」オフでも不透明のまま
                BackgroundOpacity = settings.BackgroundEnabled ? settings.BackgroundOpacity : 1,
                FallbackBrush = new SolidColorBrush(WithOpacity(keyColor, bgOpacity)),
                FallbackCornerRadius = OverlayConstants.CustomCornerRadius * scale,
                Child = new Border
                {
                    Padding = new Thickness(paddingH, paddingV, paddingH, paddingV),
                    Child = text,
                },
            };
        }

        return new Border
        {
            CornerRadius = new CornerRadius(OverlayConstants.SimpleCornerRadius * scale),
            Background = new SolidColorBrush(WithOpacity(keyColor, bgOpacity)),
            Padding = new Thickness(paddingH, paddingV, paddingH, paddingV),
            Child = text,
        };
    }

    // ── キーキャップ ─────────────────────────────────────

    private static UIElement BuildKeycapRow(
        IReadOnlyList<string> tokens, bool isTyping, int count,
        AppSettings settings, KeyFormatter formatter, bool animateAppend)
    {
        var scale = settings.DisplayScale;
        var showPlus = settings.PlusSeparator && !isTyping;
        var textBrush = new SolidColorBrush(TextColor(settings));

        var panel = new FlowPanel { Spacing = OverlayConstants.KeycapSpacing * scale };
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0 && showPlus)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontFamily = new FontFamily(OverlayConstants.FontFamilyName),
                    FontSize = OverlayConstants.PlusFontSize * scale,
                    FontWeight = FontWeights.Bold,
                    Foreground = textBrush,
                });
            }
            panel.Children.Add(BuildKeycap(tokens[i], settings, formatter));
        }
        if (count > 1)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"×{count}",
                FontFamily = new FontFamily(OverlayConstants.FontFamilyName),
                FontSize = OverlayConstants.KeycapCountFontSize * scale,
                FontWeight = FontWeights.Heavy,
                Foreground = textBrush,
            });
        }
        // タイピング連結で増えた末尾のキーを spring で出現させる
        // (Mac 版の entry.tokens への spring アニメーションに相当)
        if (animateAppend && panel.Children.Count > 0 &&
            panel.Children[panel.Children.Count - 1] is FrameworkElement appended)
        {
            AnimateKeycapEntrance(appended);
        }
        return panel;
    }

    private static void AnimateKeycapEntrance(FrameworkElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.8);
        var scale = new System.Windows.Media.ScaleTransform(0.4, 0.4);
        element.RenderTransform = scale;
        var spring = new SpringEase
        {
            Response = OverlayConstants.InsertSpringResponse,
            DampingFraction = OverlayConstants.InsertSpringDamping,
        };
        var grow = new System.Windows.Media.Animation.DoubleAnimation(
            0.4, 1.0, SpringEase.DurationFor(OverlayConstants.InsertSpringResponse))
        {
            EasingFunction = spring,
        };
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow.Clone());
        element.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.12)));
    }

    private static UIElement BuildKeycap(string token, AppSettings settings, KeyFormatter formatter)
    {
        var scale = settings.DisplayScale;
        var fontSize = OverlayConstants.KeycapFontSize * scale;
        var keyColor = KeyColor(settings);
        // 背景オフでもキー形が完全に消えないよう 0.15 を下限にする (Mac 版準拠)
        var opacity = settings.BackgroundEnabled ? settings.BackgroundOpacity : 0.15;
        var corner = new CornerRadius(OverlayConstants.KeycapCornerRadius * scale);
        var thickness = OverlayConstants.KeycapThicknessOffset * scale;

        var label = token;
        if (KeyFormatter.IsClickToken(token))
        {
            var (glyph, suffix) = ClickGlyph(token);
            label = glyph + (suffix ?? "");
        }
        else if (settings.GlobeOnImeKeys && formatter.IsImeSwitchToken(token))
        {
            label = "\U0001F310︎" + token;
        }

        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily(OverlayConstants.FontFamilyName),
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(TextColor(settings)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MinWidth = OverlayConstants.KeycapMinWidth * scale,
        };

        // 下側の縁 (キーの厚み)。キートップと同じ矩形を下へずらして減光する
        var bottom = new Border
        {
            CornerRadius = corner,
            Background = new SolidColorBrush(WithOpacity(Darken(keyColor, 0.82), opacity)),
            RenderTransform = new TranslateTransform(0, thickness),
        };
        var top = new Border
        {
            CornerRadius = corner,
            BorderBrush = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), // 白 22%
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(
                WithOpacity(keyColor, opacity),
                WithOpacity(keyColor, opacity * 0.85),
                90),
            Padding = new Thickness(
                OverlayConstants.KeycapPadding * scale, OverlayConstants.KeycapPadding * scale,
                OverlayConstants.KeycapPadding * scale, OverlayConstants.KeycapPadding * scale),
            Child = text,
        };

        var grid = new Grid
        {
            // 厚みのぶんの余白を下に確保する (次の行と重ならないように)
            Margin = new Thickness(0, 0, 0, thickness),
        };
        grid.Children.Add(bottom);
        grid.Children.Add(top);
        return grid;
    }
}
