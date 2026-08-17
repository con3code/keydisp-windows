using KeyDisp.Core.Formatting;
using KeyDisp.Core.Input;
using KeyDisp.Core.Settings;
using KeyDisp.Core.Tests.TestSupport;

namespace KeyDisp.Core.Tests.Formatting;

public class KeyFormatterTests
{
    private readonly AppSettings _settings = new();
    private readonly FakeKeyboardLayout _layout = new();
    private KeyFormatter Formatter => new(_settings, _layout);

    // ── 表記スタイル ─────────────────────────────────────

    [Fact]
    public void Localized_WindowsStyle_MapsMacSymbols()
    {
        Assert.Equal("Ctrl", Formatter.Localized("⌘"));
        Assert.Equal("Enter", Formatter.Localized("↩"));
        Assert.Equal("無変換", Formatter.Localized("英数"));
        Assert.Equal("Win", Formatter.Localized("Win")); // 対応が無いものはそのまま
    }

    [Fact]
    public void Localized_MacStyle_KeepsSymbols()
    {
        _settings.OSLabelStyle = OSLabelStyle.Mac;
        Assert.Equal("⌘", Formatter.Localized("⌘"));
        Assert.Equal("↩", Formatter.Localized("↩"));
    }

    [Fact]
    public void Localized_BothStyle_ShowsWindowsFirst()
    {
        _settings.OSLabelStyle = OSLabelStyle.Both;
        Assert.Equal("Ctrl/⌘", Formatter.Localized("⌘"));
        Assert.Equal("Win", Formatter.Localized("Win")); // 同一表示は併記しない
    }

    [Fact]
    public void Localized_JisABCLabels_ReplacesKanaEisu()
    {
        _settings.JisABCLabels = true;
        _settings.OSLabelStyle = OSLabelStyle.Mac;
        Assert.Equal("ABC", Formatter.Localized("英数"));
        Assert.Equal("あいう", Formatter.Localized("かな"));
        // Windows 表記では物理キー名 (無変換) のまま
        _settings.OSLabelStyle = OSLabelStyle.Windows;
        Assert.Equal("無変換", Formatter.Localized("英数"));
    }

    // ── 修飾キー ─────────────────────────────────────────

    [Fact]
    public void ModifierTokens_UsesStandardOrder()
    {
        var tokens = Formatter.ModifierTokens(ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Win);
        Assert.Equal(new[] { "Win", "Ctrl", "Shift" }, tokens);
    }

    [Fact]
    public void ModifierTokens_PressOrderOption_ReordersByPressSequence()
    {
        _settings.ModifierPressOrder = true;
        var tokens = Formatter.ModifierTokens(
            ModifierKeys.Shift | ModifierKeys.Control,
            new[] { ModifierKeys.Shift, ModifierKeys.Control });
        Assert.Equal(new[] { "Shift", "Ctrl" }, tokens);
    }

    [Fact]
    public void ModifierTokens_PressOrderIgnoredWhenOptionOff()
    {
        var tokens = Formatter.ModifierTokens(
            ModifierKeys.Shift | ModifierKeys.Control,
            new[] { ModifierKeys.Shift, ModifierKeys.Control });
        Assert.Equal(new[] { "Ctrl", "Shift" }, tokens);
    }

    [Fact]
    public void ModifierOf_MapsLeftRightVariants()
    {
        Assert.Equal(ModifierKeys.Control, KeyFormatter.ModifierOf(Vk.LControl));
        Assert.Equal(ModifierKeys.Control, KeyFormatter.ModifierOf(Vk.RControl));
        Assert.Equal(ModifierKeys.Alt, KeyFormatter.ModifierOf(Vk.RMenu));
        Assert.Equal(ModifierKeys.Win, KeyFormatter.ModifierOf(Vk.LWin));
        Assert.Equal(ModifierKeys.None, KeyFormatter.ModifierOf(0x41));
    }

    // ── ラベル ───────────────────────────────────────────

    [Fact]
    public void KeyLabel_FunctionKeys()
    {
        Assert.Equal("F1", Formatter.KeyLabel(Vk.F1, shifted: false));
        Assert.Equal("F12", Formatter.KeyLabel(0x7B, shifted: false));
        Assert.Equal("F24", Formatter.KeyLabel(Vk.F24, shifted: false));
    }

    [Fact]
    public void KeyLabel_SpecialKeys_FollowLabelStyle()
    {
        Assert.Equal("Enter", Formatter.KeyLabel(Vk.Enter, shifted: false));
        Assert.Equal("␣", Formatter.KeyLabel(Vk.Space, shifted: false, applyLabelStyle: false));
        _settings.OSLabelStyle = OSLabelStyle.Mac;
        Assert.Equal("↩", Formatter.KeyLabel(Vk.Enter, shifted: false));
    }

    [Fact]
    public void KeyLabel_CharacterKeys_UppercasedByDefault()
    {
        Assert.Equal("A", Formatter.KeyLabel(0x41, shifted: false));
        Assert.Equal("a", Formatter.KeyLabel(0x41, shifted: false, preserveCase: true));
        Assert.Equal("A", Formatter.KeyLabel(0x41, shifted: true, preserveCase: true));
    }

    [Fact]
    public void KeyLabel_UnknownVk_FallsBackToKeyNumber()
    {
        Assert.Equal("key231", Formatter.KeyLabel(0xE7, shifted: false));
    }

    // ── 分類 ─────────────────────────────────────────────

    [Fact]
    public void IsCharacterKey_Classification()
    {
        Assert.True(KeyFormatter.IsCharacterKey(0x41));  // A
        Assert.True(KeyFormatter.IsCharacterKey(0x31));  // 1
        Assert.True(KeyFormatter.IsCharacterKey(0xBC));  // OEM_COMMA
        Assert.False(KeyFormatter.IsCharacterKey(Vk.Enter));
        Assert.False(KeyFormatter.IsCharacterKey(Vk.Space));
        Assert.False(KeyFormatter.IsCharacterKey(Vk.LControl));
        Assert.False(KeyFormatter.IsCharacterKey(Vk.F1));
    }

    // ── JIS かな ─────────────────────────────────────────

    [Fact]
    public void KanaLabel_BasicMapping()
    {
        Assert.Equal("あ", Formatter.KanaLabel(0x33, shifted: false)); // 3
        Assert.Equal("ぁ", Formatter.KanaLabel(0x33, shifted: true));
        Assert.Equal("た", Formatter.KanaLabel(0x51, shifted: false)); // Q
        Assert.Equal("き", Formatter.KanaLabel(0x47, shifted: false)); // G
        Assert.Equal("く", Formatter.KanaLabel(0x48, shifted: false)); // H
        Assert.Equal("「", Formatter.KanaLabel(0xDB, shifted: true));  // [
        Assert.Equal("、", Formatter.KanaLabel(0xBC, shifted: true));  // ,
        Assert.Null(Formatter.KanaLabel(Vk.Enter, shifted: false));
    }

    [Fact]
    public void KanaLabel_ShiftFallsBackToNormal()
    {
        Assert.Equal("ぬ", Formatter.KanaLabel(0x31, shifted: true)); // Shift 指定なし → 通常文字
    }

    // ── クリックトークン ─────────────────────────────────

    [Fact]
    public void ClickTokens()
    {
        Assert.Equal("«click»", KeyFormatter.ClickToken(0));
        Assert.Equal("«rclick»", KeyFormatter.ClickToken(1));
        Assert.Equal("«mclick»", KeyFormatter.ClickToken(2));
        Assert.True(KeyFormatter.IsClickToken("«click»"));
        Assert.False(KeyFormatter.IsClickToken("C"));
    }
}
