namespace KeyDisp.App.Overlay;

/// <summary>
/// 描画の寸法係数。Mac 版 (SF Rounded 基準) の値を Segoe UI 向けの仮置きとして流用している。
/// 実機での見た目調整はこのファイルと Core の OverlayMetrics だけを触ること
/// (係数を散らさない — 計画のリスク緩和策)。
/// </summary>
internal static class OverlayConstants
{
    public const string FontFamilyName = "Segoe UI";

    // 共通 (表示倍率 1 のときの値。使用時に displayScale を掛ける)
    public const double BodyFontSize = 34;
    public const double RowSpacing = 8;       // OverlayMetrics.RowSpacing と揃える
    public const double OutlineWidth = 1.4;

    // シンプルスタイル
    public const double SimplePaddingH = 14;
    public const double SimplePaddingV = 7;
    public const double SimpleCornerRadius = 14;
    public const double CountFontScale = 0.6; // ×n は本文の 0.6 倍

    // キーキャップスタイル
    public const double KeycapFontSize = 30;
    public const double KeycapMinWidth = 38;
    public const double KeycapPadding = 7;
    public const double KeycapCornerRadius = 10;
    public const double KeycapThicknessOffset = 3.5; // 下側の縁 (キーの厚み)
    public const double KeycapSpacing = 5;
    public const double PlusFontSize = 20;
    public const double KeycapCountFontSize = 22;

    // カスタム画像スタイル
    public const double CustomPaddingH = 22;
    public const double CustomPaddingV = 10;
    public const double CustomCornerRadius = 12;

    // アニメーション
    public const double InsertSpringResponse = 0.28;
    public const double InsertSpringDamping = 0.85;
    public const double PulseSpringResponse = 0.25;
    public const double PulseSpringDamping = 0.55;
    public const double PulseScale = 1.1;
    public const double PulseThrottleMs = 120; // autorepeat の高頻度更新の間引き
}
