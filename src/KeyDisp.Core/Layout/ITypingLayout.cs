namespace KeyDisp.Core.Layout;

/// <summary>
/// タイピング行の折り返し判定 (Mac 版 OverlayMetrics.typingLineFits / usesTypewriterWrap 相当)。
/// キーキャップ・カスタム画像スタイルはキーが独立して並ぶので、端まで来たら
/// 行を改めて先頭から続ける (タイプライター式)。シンプル表示は折り返しに任せる。
/// </summary>
public interface ITypingLayout
{
    /// <summary>現在のスタイルがタイプライター式か。</summary>
    bool UsesTypewriterWrap { get; }

    /// <summary>トークン列が現在のオーバーレイ内側幅に収まるか (実測)。</summary>
    bool TypingLineFits(IReadOnlyList<string> tokens);
}

/// <summary>常に収まる扱いにする実装 (シンプルスタイル相当・テスト用)。</summary>
public sealed class UnlimitedTypingLayout : ITypingLayout
{
    public bool UsesTypewriterWrap => false;
    public bool TypingLineFits(IReadOnlyList<string> tokens) => true;
}
