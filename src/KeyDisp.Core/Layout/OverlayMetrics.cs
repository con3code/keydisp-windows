using KeyDisp.Core.Display;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Layout;

/// <summary>表示領域に収まる 1 行ぶんの表示内容 (トークンは切り詰められている場合がある)。</summary>
public readonly record struct VisibleRow(KeyEntry Entry, IReadOnlyList<string> Tokens);

/// <summary>
/// キー表示の寸法計算 (Mac 版 OverlayRootView.swift の OverlayMetrics の移植)。
/// 表示側とウィンドウ側で同じ値を使うためにここへまとめる。
/// 寸法係数は Mac 版 (SF Rounded 基準) の値を仮置きしており、
/// Windows 実機でのフォント (Segoe UI) に合わせた一括調整を予定
/// (DEVICE-TEST-CHECKLIST.md B 項)。
/// </summary>
public sealed class OverlayMetrics : ITypingLayout
{
    private readonly AppSettings _settings;
    private readonly ITextMeasurer _measurer;
    private readonly Dictionary<string, double> _keycapWidthCache = new();
    private readonly Dictionary<string, double> _textWidthCache = new();

    public OverlayMetrics(AppSettings settings, ITextMeasurer measurer)
    {
        _settings = settings;
        _measurer = measurer;
    }

    /// <summary>表示領域の内側の余白 (上下合計)。</summary>
    public const double Padding = 32;

    /// <summary>折り返しのないキー表示 1 行ぶんの高さの目安。</summary>
    public double RowHeight()
    {
        var scale = _settings.DisplayScale;
        return _settings.KeyStyle switch
        {
            KeyStyle.Keycap => 56 * scale,      // 文字 30pt + padding 7pt×2 + 厚み
            KeyStyle.CustomImage => 64 * scale, // 文字 34pt + padding 10pt×2
            _ => 58 * scale,                    // simple: 文字 34pt + padding 7pt×2
        };
    }

    public double RowSpacing() => 8 * _settings.DisplayScale;

    /// <summary>指定した行数を表示するのに必要な高さ。</summary>
    public double RequiredHeight(int rows)
    {
        var n = Math.Max(1, rows);
        return n * RowHeight() + (n - 1) * RowSpacing() + Padding;
    }

    /// <summary>
    /// トークン 1 個ぶんの幅の目安 (折り返し位置の計算用)。
    /// 「Ctrl」「BackSpace」のような複数文字のラベルは記号 1 つのキーより
    /// ずっと横に広いので、ラベルを実際に測って見積もる。
    /// </summary>
    public double TokenWidth(string token)
    {
        var scale = _settings.DisplayScale;
        return _settings.KeyStyle == KeyStyle.Keycap
            ? KeycapWidth(token) * scale
            : TextWidth(token) * scale;
    }

    /// <summary>表示倍率 1 のときのキーキャップ 1 個ぶんの幅 (隣との間隔込み)。</summary>
    private double KeycapWidth(string token)
    {
        if (_keycapWidthCache.TryGetValue(token, out var cached)) return cached;
        var textWidth = _measurer.MeasureKeycapText(token);
        // KeycapView の最小幅 38 + 左右余白 7×2 + 隣との間隔 5
        var w = Math.Max(38, textWidth) + 14 + 5;
        _keycapWidthCache[token] = w;
        return w;
    }

    private double TextWidth(string token)
    {
        if (_textWidthCache.TryGetValue(token, out var cached)) return cached;
        var w = _measurer.MeasureText(token);
        _textWidthCache[token] = w;
        return w;
    }

    /// <summary>キーとキーの間の区切りが占める幅 (「+」または細いスペース)。</summary>
    public double SeparatorWidth()
    {
        var scale = _settings.DisplayScale;
        if (_settings.KeyStyle == KeyStyle.Keycap)
        {
            return _settings.PlusSeparator ? 18 * scale : 0;
        }
        return TextWidth(_settings.PlusSeparator ? "+" : " ") * scale;
    }

    /// <summary>
    /// キーが 1 つずつ独立して並ぶスタイルかどうか。
    /// この場合は文字を流し込む折り返しではなく、行を改めるタイプライター式が合う。
    /// </summary>
    public bool UsesTypewriterWrap => _settings.KeyStyle is KeyStyle.Keycap or KeyStyle.CustomImage;

    /// <summary>タイプライター式の行に、このトークン列が 1 行のまま収まるかどうか。</summary>
    public bool TypingLineFits(IReadOnlyList<string> tokens, double width)
    {
        var needed = tokens.Sum(TokenWidth);
        return _settings.KeyStyle switch
        {
            KeyStyle.Keycap => needed <= width,
            // 行の背景の内側余白 (左右 22 ずつ) のぶんだけ文字の領域は狭い
            KeyStyle.CustomImage => needed <= width - 44 * _settings.DisplayScale,
            _ => true,
        };
    }

    bool ITypingLayout.TypingLineFits(IReadOnlyList<string> tokens) =>
        TypingLineFits(tokens, _settings.OverlayContentWidth);

    /// <summary>折り返して増えた 1 行ぶんの高さ。</summary>
    public double ExtraLineHeight()
    {
        var scale = _settings.DisplayScale;
        return _settings.KeyStyle == KeyStyle.Keycap ? 55 * scale : 41 * scale;
    }

    /// <summary>
    /// トークンを順に並べて、折り返して何行になるかを見積もる。
    /// 計測と配置で必ず同じこの判定を使うこと (Mac 版 FlowLayout の二重判定バグの教訓)。
    /// </summary>
    public int WrappedLines(IReadOnlyList<string> tokens, bool isTyping, double width)
    {
        var gap = isTyping ? 0 : SeparatorWidth();
        var lines = 1;
        double used = 0;
        foreach (var token in tokens)
        {
            var w = TokenWidth(token);
            var need = used > 0 ? gap + w : w;
            if (used > 0 && used + need > width)
            {
                lines += 1;
                used = w;
            }
            else
            {
                used += need;
            }
        }
        return lines;
    }

    /// <summary>1 行が折り返しも含めて占める高さ。</summary>
    public double HeightOf(KeyEntry entry, double width)
    {
        var lines = WrappedLines(entry.Tokens, entry.IsTyping, width);
        return RowHeight() + (lines - 1) * ExtraLineHeight();
    }

    /// <summary>
    /// 表示領域に収まる行だけを返す (新しい方を優先し、古い行から落とす)。
    /// いちばん新しい行だけで収まらない場合は、その行の古い文字を落として収める。
    /// </summary>
    public List<VisibleRow> VisibleRows(IReadOnlyList<KeyEntry> entries, double width, double height)
    {
        if (entries.Count == 0) return new List<VisibleRow>();
        var availH = Math.Max(1, height - Padding);
        var availW = Math.Max(1, width - Padding);
        var result = new List<VisibleRow>();
        double used = 0;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            var h = HeightOf(entry, availW);
            var need = result.Count == 0 ? h : h + RowSpacing();
            if (used + need > availH)
            {
                if (result.Count == 0)
                {
                    // 1 行だけでも収まらない: 新しい文字を残して先頭を削る
                    var maxLines = Math.Max(1, (int)((availH - RowHeight()) / ExtraLineHeight()) + 1);
                    var trimmed = TokensFitting(entry.Tokens, entry.IsTyping, maxLines, availW);
                    result.Add(new VisibleRow(entry, trimmed));
                }
                break;
            }
            used += need;
            result.Insert(0, new VisibleRow(entry, entry.Tokens));
        }
        return result;
    }

    /// <summary>
    /// 指定した行数に収まるいちばん長い末尾を返す (新しいキーほど残す)。
    /// 末尾を伸ばすほど行数は減らないので、二分探索で境目を求める。
    /// </summary>
    private IReadOnlyList<string> TokensFitting(
        IReadOnlyList<string> tokens, bool isTyping, int lines, double width)
    {
        if (tokens.Count <= 1) return tokens;
        int lo = 1, hi = tokens.Count;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var suffix = tokens.Skip(tokens.Count - mid).ToList();
            if (WrappedLines(suffix, isTyping, width) <= lines) lo = mid;
            else hi = mid - 1;
        }
        return tokens.Skip(tokens.Count - lo).ToList();
    }
}
