using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyDisp.Core.Settings;

/// <summary>画面別プロファイル (編集 HUD にある 10 項目 + フレーム)。docs/SPEC.md §5。</summary>
public sealed class ScreenProfileDocument
{
    public double[]? Frame { get; set; } // [x, y, width, height] (物理 px)
    public int? Style { get; set; }
    public double? DisplayScale { get; set; }
    public double? MaxRows { get; set; }
    public bool? StackFromTop { get; set; }
    public int? RowAlignment { get; set; }
    public string? TextColorHex { get; set; }
    public string? KeyColorHex { get; set; }
    public bool? BackgroundEnabled { get; set; }
    public double? BackgroundOpacity { get; set; }
    public bool? Hidden { get; set; }
}

/// <summary>
/// settings.json のスキーマ (version 1)。キー名は Mac 版 UserDefaults を camelCase で踏襲
/// (意図的な差分は docs/SPEC.md §10)。プロパティが欠けていても既定値で補完される
/// (nullable + Apply 時のフォールバック)。
/// </summary>
public sealed class SettingsDocument
{
    public int Version { get; set; } = 1;

    // 表示
    public bool? OverlayVisible { get; set; }
    public double? DisplayScale { get; set; }
    public double? HoldDuration { get; set; }
    public double? FadeDuration { get; set; }
    public double? MaxRows { get; set; }
    public bool? StackFromTop { get; set; }
    public bool? ShowAllKeys { get; set; }
    public bool? CountRepeats { get; set; }
    public bool? StepModifierRelease { get; set; }
    public double? HoldJudgeDelay { get; set; }
    public bool? TopEdgeFreeze { get; set; }
    public bool? DragToMove { get; set; }
    public bool? TypingAnimation { get; set; }
    public bool? FollowCursorScreen { get; set; }
    // デザイン
    public int? KeyStyle { get; set; }
    public int? RowAlignment { get; set; }
    public string? TextColorHex { get; set; }
    public bool? TextOutline { get; set; }
    public string? TextOutlineColorHex { get; set; }
    public string? KeyColorHex { get; set; }
    public double? BackgroundOpacity { get; set; }
    public bool? BackgroundEnabled { get; set; }
    public string? CustomImagePath { get; set; }
    // キー表記
    public int? OsLabelStyle { get; set; }
    public bool? JisABCLabels { get; set; }
    public bool? PlusSeparator { get; set; }
    public bool? DistinguishCase { get; set; }
    public bool? KanaDisplay { get; set; }
    public bool? GlobeOnImeKeys { get; set; }
    public bool? ModifierPressOrder { get; set; }
    public int? ArrowGrouping { get; set; }
    public bool? ShowKeyClickCombo { get; set; }
    // マウス
    public bool? MouseHighlight { get; set; }
    public string? MouseColorHex { get; set; }
    public double? MouseHighlightSize { get; set; }
    public bool? ShowClickInKeyDisplay { get; set; }
    public bool? BigCursor { get; set; }
    public double? BigCursorSize { get; set; }
    public string? BigCursorColorHex { get; set; }
    // 一般
    public int? Language { get; set; }
    public bool? HotCornerHide { get; set; }
    public bool? LaunchAtLogin { get; set; }
    public int? HotKeyVk { get; set; }
    public int? HotKeyModifiers { get; set; }

    /// <summary>オーバーレイの現在フレーム [x, y, width, height] (物理 px)。</summary>
    public double[]? OverlayFrame { get; set; }

    /// <summary>画面 ID (安定 ID) → プロファイル。</summary>
    public Dictionary<string, ScreenProfileDocument>? DisplayProfiles { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SettingsDocument FromJson(string json) =>
        JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions) ?? new SettingsDocument();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>ドキュメントの値を設定へ流し込む。欠けている項目は現在値 (=既定値) を保つ。</summary>
    public void Apply(AppSettings s)
    {
        s.OverlayVisible = OverlayVisible ?? s.OverlayVisible;
        s.DisplayScale = DisplayScale ?? s.DisplayScale;
        s.HoldDuration = HoldDuration ?? s.HoldDuration;
        s.FadeDuration = FadeDuration ?? s.FadeDuration;
        s.MaxRows = MaxRows ?? s.MaxRows;
        s.StackFromTop = StackFromTop ?? s.StackFromTop;
        s.ShowAllKeys = ShowAllKeys ?? s.ShowAllKeys;
        s.CountRepeats = CountRepeats ?? s.CountRepeats;
        s.StepModifierRelease = StepModifierRelease ?? s.StepModifierRelease;
        s.HoldJudgeDelay = HoldJudgeDelay ?? s.HoldJudgeDelay;
        s.TopEdgeFreeze = TopEdgeFreeze ?? s.TopEdgeFreeze;
        s.DragToMove = DragToMove ?? s.DragToMove;
        s.TypingAnimation = TypingAnimation ?? s.TypingAnimation;
        s.FollowCursorScreen = FollowCursorScreen ?? s.FollowCursorScreen;
        s.KeyStyle = (KeyStyle?)KeyStyle ?? s.KeyStyle;
        s.RowAlignment = (RowAlignment?)RowAlignment ?? s.RowAlignment;
        s.TextColorHex = TextColorHex ?? s.TextColorHex;
        s.TextOutline = TextOutline ?? s.TextOutline;
        s.TextOutlineColorHex = TextOutlineColorHex ?? s.TextOutlineColorHex;
        s.KeyColorHex = KeyColorHex ?? s.KeyColorHex;
        s.BackgroundOpacity = BackgroundOpacity ?? s.BackgroundOpacity;
        s.BackgroundEnabled = BackgroundEnabled ?? s.BackgroundEnabled;
        s.CustomImagePath = CustomImagePath ?? s.CustomImagePath;
        s.OSLabelStyle = (OSLabelStyle?)OsLabelStyle ?? s.OSLabelStyle;
        s.JisABCLabels = JisABCLabels ?? s.JisABCLabels;
        s.PlusSeparator = PlusSeparator ?? s.PlusSeparator;
        s.DistinguishCase = DistinguishCase ?? s.DistinguishCase;
        s.KanaDisplay = KanaDisplay ?? s.KanaDisplay;
        s.GlobeOnImeKeys = GlobeOnImeKeys ?? s.GlobeOnImeKeys;
        s.ModifierPressOrder = ModifierPressOrder ?? s.ModifierPressOrder;
        s.ArrowGrouping = (ArrowGrouping?)ArrowGrouping ?? s.ArrowGrouping;
        s.ShowKeyClickCombo = ShowKeyClickCombo ?? s.ShowKeyClickCombo;
        s.MouseHighlight = MouseHighlight ?? s.MouseHighlight;
        s.MouseColorHex = MouseColorHex ?? s.MouseColorHex;
        s.MouseHighlightSize = MouseHighlightSize ?? s.MouseHighlightSize;
        s.ShowClickInKeyDisplay = ShowClickInKeyDisplay ?? s.ShowClickInKeyDisplay;
        s.BigCursor = BigCursor ?? s.BigCursor;
        s.BigCursorSize = BigCursorSize ?? s.BigCursorSize;
        s.BigCursorColorHex = BigCursorColorHex ?? s.BigCursorColorHex;
        s.Language = (AppLanguage?)Language ?? s.Language;
        s.HotCornerHide = HotCornerHide ?? s.HotCornerHide;
        s.LaunchAtLogin = LaunchAtLogin ?? s.LaunchAtLogin;
        s.HotKeyVk = HotKeyVk ?? s.HotKeyVk;
        s.HotKeyModifiers = HotKeyModifiers ?? s.HotKeyModifiers;
    }

    /// <summary>現在の設定からドキュメントを作る (displayProfiles は呼び出し側が別途保持)。</summary>
    public static SettingsDocument From(AppSettings s) => new()
    {
        Version = 1,
        OverlayVisible = s.OverlayVisible,
        DisplayScale = s.DisplayScale,
        HoldDuration = s.HoldDuration,
        FadeDuration = s.FadeDuration,
        MaxRows = s.MaxRows,
        StackFromTop = s.StackFromTop,
        ShowAllKeys = s.ShowAllKeys,
        CountRepeats = s.CountRepeats,
        StepModifierRelease = s.StepModifierRelease,
        HoldJudgeDelay = s.HoldJudgeDelay,
        TopEdgeFreeze = s.TopEdgeFreeze,
        DragToMove = s.DragToMove,
        TypingAnimation = s.TypingAnimation,
        FollowCursorScreen = s.FollowCursorScreen,
        KeyStyle = (int)s.KeyStyle,
        RowAlignment = (int)s.RowAlignment,
        TextColorHex = s.TextColorHex,
        TextOutline = s.TextOutline,
        TextOutlineColorHex = s.TextOutlineColorHex,
        KeyColorHex = s.KeyColorHex,
        BackgroundOpacity = s.BackgroundOpacity,
        BackgroundEnabled = s.BackgroundEnabled,
        CustomImagePath = s.CustomImagePath,
        OsLabelStyle = (int)s.OSLabelStyle,
        JisABCLabels = s.JisABCLabels,
        PlusSeparator = s.PlusSeparator,
        DistinguishCase = s.DistinguishCase,
        KanaDisplay = s.KanaDisplay,
        GlobeOnImeKeys = s.GlobeOnImeKeys,
        ModifierPressOrder = s.ModifierPressOrder,
        ArrowGrouping = (int)s.ArrowGrouping,
        ShowKeyClickCombo = s.ShowKeyClickCombo,
        MouseHighlight = s.MouseHighlight,
        MouseColorHex = s.MouseColorHex,
        MouseHighlightSize = s.MouseHighlightSize,
        ShowClickInKeyDisplay = s.ShowClickInKeyDisplay,
        BigCursor = s.BigCursor,
        BigCursorSize = s.BigCursorSize,
        BigCursorColorHex = s.BigCursorColorHex,
        Language = (int)s.Language,
        HotCornerHide = s.HotCornerHide,
        LaunchAtLogin = s.LaunchAtLogin,
        HotKeyVk = s.HotKeyVk,
        HotKeyModifiers = s.HotKeyModifiers,
    };
}
