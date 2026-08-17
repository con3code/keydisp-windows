using KeyDisp.Core.Display;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Tests.Layout;

/// <summary>1 文字 = 10px の決定的なフェイク測定器。</summary>
file sealed class FakeMeasurer : ITextMeasurer
{
    public double MeasureKeycapText(string token) => token.Length * 10;
    public double MeasureText(string token) => token.Length * 10;
}

public class OverlayMetricsTests
{
    private readonly AppSettings _settings = new();
    private readonly OverlayMetrics _metrics;

    public OverlayMetricsTests()
    {
        _metrics = new OverlayMetrics(_settings, new FakeMeasurer());
    }

    private static KeyEntry Entry(bool isTyping, params string[] tokens) =>
        new(tokens, isTyping, KeyEntryPhase.Active);

    [Fact]
    public void RowHeight_DependsOnStyleAndScale()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        Assert.Equal(58, _metrics.RowHeight());
        _settings.KeyStyle = KeyStyle.Keycap;
        Assert.Equal(56, _metrics.RowHeight());
        _settings.KeyStyle = KeyStyle.CustomImage;
        _settings.DisplayScale = 2.0;
        Assert.Equal(128, _metrics.RowHeight());
    }

    [Fact]
    public void RequiredHeight_IncludesSpacingAndPadding()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        // 3 行: 3*58 + 2*8 + 32 = 222
        Assert.Equal(222, _metrics.RequiredHeight(3));
    }

    [Fact]
    public void KeycapTokenWidth_HasMinimumWidth()
    {
        _settings.KeyStyle = KeyStyle.Keycap;
        // 1 文字 (10px) は最小幅 38 が効く: 38+14+5 = 57
        Assert.Equal(57, _metrics.TokenWidth("A"));
        // 「BackSpace」(9 文字 = 90px): 90+14+5 = 109
        Assert.Equal(109, _metrics.TokenWidth("BackSpace"));
    }

    [Fact]
    public void UsesTypewriterWrap_OnlyForKeycapAndCustomImage()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        Assert.False(_metrics.UsesTypewriterWrap);
        _settings.KeyStyle = KeyStyle.Keycap;
        Assert.True(_metrics.UsesTypewriterWrap);
        _settings.KeyStyle = KeyStyle.CustomImage;
        Assert.True(_metrics.UsesTypewriterWrap);
    }

    [Fact]
    public void TypingLineFits_KeycapComparesAgainstFullWidth()
    {
        _settings.KeyStyle = KeyStyle.Keycap;
        // 3 キー × 57 = 171
        Assert.True(_metrics.TypingLineFits(new[] { "A", "B", "C" }, width: 171));
        Assert.False(_metrics.TypingLineFits(new[] { "A", "B", "C" }, width: 170));
    }

    [Fact]
    public void TypingLineFits_CustomImageSubtractsInnerPadding()
    {
        _settings.KeyStyle = KeyStyle.CustomImage;
        // 3 文字 × 10 = 30。内側余白 44 を引いた幅と比較
        Assert.True(_metrics.TypingLineFits(new[] { "A", "B", "C" }, width: 74));
        Assert.False(_metrics.TypingLineFits(new[] { "A", "B", "C" }, width: 73));
    }

    [Fact]
    public void TypingLineFits_SimpleAlwaysFits()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        Assert.True(_metrics.TypingLineFits(new[] { "A", "B", "C", "D", "E" }, width: 1));
    }

    [Fact]
    public void WrappedLines_CountsLineBreaks()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        _settings.PlusSeparator = false;
        // タイピング (gap 0): 各 10px、幅 25 → 2 個ずつで折り返し
        Assert.Equal(1, _metrics.WrappedLines(new[] { "A", "B" }, isTyping: true, width: 25));
        Assert.Equal(2, _metrics.WrappedLines(new[] { "A", "B", "C" }, isTyping: true, width: 25));
        Assert.Equal(3, _metrics.WrappedLines(new[] { "A", "B", "C", "D", "E" }, isTyping: true, width: 25));
    }

    [Fact]
    public void WrappedLines_ComboIncludesSeparatorGap()
    {
        _settings.KeyStyle = KeyStyle.Keycap;
        _settings.PlusSeparator = true; // 区切り幅 18
        // キー 57 + 18 + 57 = 132
        Assert.Equal(1, _metrics.WrappedLines(new[] { "A", "B" }, isTyping: false, width: 132));
        Assert.Equal(2, _metrics.WrappedLines(new[] { "A", "B" }, isTyping: false, width: 131));
    }

    [Fact]
    public void VisibleRows_KeepsNewestRows()
    {
        _settings.KeyStyle = KeyStyle.Simple; // 行高 58、間隔 8
        var entries = new[]
        {
            Entry(false, "1"),
            Entry(false, "2"),
            Entry(false, "3"),
        };
        // availH = 182 - 32 = 150 → 58 + (58+8) = 124 で 2 行まで
        var rows = _metrics.VisibleRows(entries, width: 620, height: 182);
        Assert.Equal(2, rows.Count);
        Assert.Equal("2", rows[0].Entry.Text);
        Assert.Equal("3", rows[1].Entry.Text);
    }

    [Fact]
    public void VisibleRows_TrimsOldestTokensWhenSingleRowOverflows()
    {
        _settings.KeyStyle = KeyStyle.Simple;
        var longTyping = Entry(true, "A", "B", "C", "D", "E", "F", "G", "H");
        // availW = 82 - 32 = 50 → 5 文字/行。availH = 90 - 32 = 58 → 1 行のみ (extra 41 が入らない)
        var rows = _metrics.VisibleRows(new[] { longTyping }, width: 82, height: 90);
        var row = Assert.Single(rows);
        // 新しい文字 (末尾) を優先して 5 個残る
        Assert.Equal(new[] { "D", "E", "F", "G", "H" }, row.Tokens);
    }

    [Fact]
    public void VisibleRows_EmptyForNoEntries()
    {
        Assert.Empty(_metrics.VisibleRows(Array.Empty<KeyEntry>(), 620, 440));
    }
}
