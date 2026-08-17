namespace KeyDisp.Core.Layout;

/// <summary>
/// 表示倍率 1 のときのトークン幅の実測 (Mac 版は NSFont で実測していた部分)。
/// App 層は WPF の FormattedText で実装し、テストはフェイクを使う。
/// 測定は描画と同じフォント・ウェイトで行うこと (概算だと折り返し判定がずれる。
/// Mac 版 DEVLOG の「キー幅 57pt 固定」バグの教訓)。
/// </summary>
public interface ITextMeasurer
{
    /// <summary>キーキャップの文字 (30pt bold 相当) の幅。</summary>
    double MeasureKeycapText(string token);

    /// <summary>シンプル / カスタム画像の文字 (34pt semibold 相当) の幅。</summary>
    double MeasureText(string token);
}
