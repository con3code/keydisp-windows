using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KeyDisp.Core.Settings;

/// <summary>キー表示のデザインスタイル</summary>
public enum KeyStyle
{
    Simple = 0,      // シンプルな文字表示
    Keycap = 1,      // キーキャップ風デザイン
    CustomImage = 2, // ユーザー指定の背景画像
}

/// <summary>キー表示の行の水平方向の揃え</summary>
public enum RowAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>
/// キーの表記スタイル。Mac 版とは意味が反転し、Windows 表記が既定。
/// Mac 記号表記は「Windows で ⌘C を見せたい」教室用途向けのオプション。
/// </summary>
public enum OSLabelStyle
{
    Windows = 0,
    Mac = 1,
    Both = 2, // 併存表記 (例: "Ctrl/⌘")
}

/// <summary>矢印キーのグルーピング方式</summary>
public enum ArrowGrouping
{
    Simultaneous = 0, // 同時押しのみまとめる
    Consecutive = 1,  // 連続入力もまとめる
    Off = 2,          // まとめない
}

/// <summary>UI の言語</summary>
public enum AppLanguage
{
    System = 0,
    Japanese = 1,
    English = 2,
}

/// <summary>
/// アプリ設定 (Mac 版 AppSettings.swift の写像)。永続化は App 層の SettingsRepository が担い、
/// Core は純粋な状態としてのみ扱う。キー名・既定値は Mac 版 UserDefaults を踏襲
/// (意図的な差分は docs/SPEC.md §10)。
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── 表示 ──────────────────────────────────────────────

    private bool _overlayVisible = true;
    public bool OverlayVisible { get => _overlayVisible; set => Set(ref _overlayVisible, value); }

    private double _displayScale = 1.0; // 0.5〜5.0
    public double DisplayScale { get => _displayScale; set => Set(ref _displayScale, value); }

    private double _holdDuration = 1.5; // 0〜5 秒
    public double HoldDuration { get => _holdDuration; set => Set(ref _holdDuration, value); }

    private double _fadeDuration = 0.8; // 0.1〜4 秒
    public double FadeDuration { get => _fadeDuration; set => Set(ref _fadeDuration, value); }

    private double _maxRows = 4; // 1〜8
    public double MaxRows { get => _maxRows; set => Set(ref _maxRows, value); }

    private bool _stackFromTop;
    public bool StackFromTop { get => _stackFromTop; set => Set(ref _stackFromTop, value); }

    private bool _showAllKeys;
    public bool ShowAllKeys { get => _showAllKeys; set => Set(ref _showAllKeys, value); }

    private bool _countRepeats = true;
    public bool CountRepeats { get => _countRepeats; set => Set(ref _countRepeats, value); }

    private bool _stepModifierRelease;
    public bool StepModifierRelease { get => _stepModifierRelease; set => Set(ref _stepModifierRelease, value); }

    private double _holdJudgeDelay = 0.5; // 0.2〜2.0 秒
    public double HoldJudgeDelay { get => _holdJudgeDelay; set => Set(ref _holdJudgeDelay, value); }

    private bool _topEdgeFreeze;
    public bool TopEdgeFreeze { get => _topEdgeFreeze; set => Set(ref _topEdgeFreeze, value); }

    private bool _dragToMove;
    public bool DragToMove { get => _dragToMove; set => Set(ref _dragToMove, value); }

    private bool _typingAnimation = true;
    public bool TypingAnimation { get => _typingAnimation; set => Set(ref _typingAnimation, value); }

    private bool _followCursorScreen;
    public bool FollowCursorScreen { get => _followCursorScreen; set => Set(ref _followCursorScreen, value); }

    // ── デザイン ──────────────────────────────────────────

    private KeyStyle _keyStyle = KeyStyle.Keycap;
    public KeyStyle KeyStyle { get => _keyStyle; set => Set(ref _keyStyle, value); }

    private RowAlignment _rowAlignment = RowAlignment.Left;
    public RowAlignment RowAlignment { get => _rowAlignment; set => Set(ref _rowAlignment, value); }

    private string _textColorHex = "#FFFFFF";
    public string TextColorHex { get => _textColorHex; set => Set(ref _textColorHex, value); }

    private bool _textOutline;
    public bool TextOutline { get => _textOutline; set => Set(ref _textOutline, value); }

    private string _textOutlineColorHex = "#000000";
    public string TextOutlineColorHex { get => _textOutlineColorHex; set => Set(ref _textOutlineColorHex, value); }

    private string _keyColorHex = "#1C1C22";
    public string KeyColorHex { get => _keyColorHex; set => Set(ref _keyColorHex, value); }

    private double _backgroundOpacity = 0.75; // 0〜1
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, value); }

    private bool _backgroundEnabled = true;
    public bool BackgroundEnabled { get => _backgroundEnabled; set => Set(ref _backgroundEnabled, value); }

    private string _customImagePath = "";
    public string CustomImagePath { get => _customImagePath; set => Set(ref _customImagePath, value); }

    // ── キー表記 ──────────────────────────────────────────

    private OSLabelStyle _osLabelStyle = OSLabelStyle.Windows;
    public OSLabelStyle OSLabelStyle { get => _osLabelStyle; set => Set(ref _osLabelStyle, value); }

    private bool _jisABCLabels;
    public bool JisABCLabels { get => _jisABCLabels; set => Set(ref _jisABCLabels, value); }

    private bool _plusSeparator;
    public bool PlusSeparator { get => _plusSeparator; set => Set(ref _plusSeparator, value); }

    private bool _distinguishCase;
    public bool DistinguishCase { get => _distinguishCase; set => Set(ref _distinguishCase, value); }

    private bool _kanaDisplay;
    public bool KanaDisplay { get => _kanaDisplay; set => Set(ref _kanaDisplay, value); }

    private bool _globeOnImeKeys; // Mac 版はオン既定だが 🌐 キーが無いためオフ既定 (SPEC §10)
    public bool GlobeOnImeKeys { get => _globeOnImeKeys; set => Set(ref _globeOnImeKeys, value); }

    private bool _modifierPressOrder;
    public bool ModifierPressOrder { get => _modifierPressOrder; set => Set(ref _modifierPressOrder, value); }

    private ArrowGrouping _arrowGrouping = ArrowGrouping.Simultaneous;
    public ArrowGrouping ArrowGrouping { get => _arrowGrouping; set => Set(ref _arrowGrouping, value); }

    private bool _showKeyClickCombo;
    public bool ShowKeyClickCombo { get => _showKeyClickCombo; set => Set(ref _showKeyClickCombo, value); }

    // ── マウス ────────────────────────────────────────────

    private bool _mouseHighlight;
    public bool MouseHighlight { get => _mouseHighlight; set => Set(ref _mouseHighlight, value); }

    private string _mouseColorHex = "#FFB300";
    public string MouseColorHex { get => _mouseColorHex; set => Set(ref _mouseColorHex, value); }

    private double _mouseHighlightSize = 56; // 30〜120 px
    public double MouseHighlightSize { get => _mouseHighlightSize; set => Set(ref _mouseHighlightSize, value); }

    private bool _showClickInKeyDisplay = true;
    public bool ShowClickInKeyDisplay { get => _showClickInKeyDisplay; set => Set(ref _showClickInKeyDisplay, value); }

    private bool _bigCursor;
    public bool BigCursor { get => _bigCursor; set => Set(ref _bigCursor, value); }

    private double _bigCursorSize = 64; // 32〜160 px
    public double BigCursorSize { get => _bigCursorSize; set => Set(ref _bigCursorSize, value); }

    private string _bigCursorColorHex = "#FFFFFF";
    public string BigCursorColorHex { get => _bigCursorColorHex; set => Set(ref _bigCursorColorHex, value); }

    // ── 一般 ──────────────────────────────────────────────

    private AppLanguage _language = AppLanguage.System;
    public AppLanguage Language { get => _language; set => Set(ref _language, value); }

    private bool _hotCornerHide;
    public bool HotCornerHide { get => _hotCornerHide; set => Set(ref _hotCornerHide, value); }

    private bool _launchAtLogin;
    public bool LaunchAtLogin { get => _launchAtLogin; set => Set(ref _launchAtLogin, value); }

    /// <summary>グローバルホットキーの仮想キーコード (既定: K)</summary>
    private int _hotKeyVk = 0x4B;
    public int HotKeyVk { get => _hotKeyVk; set => Set(ref _hotKeyVk, value); }

    /// <summary>グローバルホットキーの修飾フラグ (Win32 MOD_* 値。既定: MOD_ALT|MOD_WIN)</summary>
    private int _hotKeyModifiers = 0x0001 | 0x0008;
    public int HotKeyModifiers { get => _hotKeyModifiers; set => Set(ref _hotKeyModifiers, value); }

    // ── 実行時のみ (非永続) ────────────────────────────────

    private bool _editMode;
    public bool EditMode { get => _editMode; set => Set(ref _editMode, value); }

    private double _overlayContentWidth = 620;
    /// <summary>オーバーレイ内側幅。折り返し判定用にウィンドウ側が publish する。</summary>
    public double OverlayContentWidth { get => _overlayContentWidth; set => Set(ref _overlayContentWidth, value); }

    private bool _hotCornerSuppressed;
    /// <summary>ホットエッジによる一時非表示中 (この間キー入力処理も止まる)。</summary>
    public bool HotCornerSuppressed { get => _hotCornerSuppressed; set => Set(ref _hotCornerSuppressed, value); }

    private bool _hiddenOnCurrentScreen;
    /// <summary>現在の画面での「表示しない」。実体は画面プロファイル側にある。</summary>
    public bool HiddenOnCurrentScreen { get => _hiddenOnCurrentScreen; set => Set(ref _hiddenOnCurrentScreen, value); }
}
