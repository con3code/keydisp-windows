using KeyDisp.Core.Display;
using KeyDisp.Core.Input;
using KeyDisp.Core.Settings;
using KeyDisp.Core.Tests.TestSupport;

namespace KeyDisp.Core.Tests.StateMachine;

/// <summary>
/// 状態機械のシナリオテスト。期待値は Mac 版 KeyCaptureController.swift の挙動
/// (docs/SPEC.md §2)。既定の表記スタイルは Windows なので Ctrl/Shift 等で表示される。
/// </summary>
public class KeyStateMachineTests
{
    private readonly StateMachineHarness _h = new();

    private const int C = 0x43;
    private const int H = 0x48;
    private const int E = 0x45;
    private const int L = 0x4C;
    private const int A = 0x41;

    // ── コンビネーション ──────────────────────────────────

    [Fact]
    public void CtrlC_ShowsSingleComboRow()
    {
        _h.Down(Vk.LControl);
        Assert.Equal("Ctrl", _h.SingleEntry().Text);

        _h.Down(C);
        var entry = _h.SingleEntry();
        Assert.Equal("CtrlC", entry.Text);
        Assert.Equal(KeyEntryPhase.Active, entry.Phase);

        _h.Up(C);
        _h.Up(Vk.LControl);
        Assert.Equal(KeyEntryPhase.Holding, _h.SingleEntry().Phase);
        Assert.Equal("CtrlC", _h.SingleEntry().Text);
    }

    [Fact]
    public void ModifierPeak_KeepsBothModifiersWhenOneReleasedQuickly()
    {
        _h.Down(Vk.LControl);
        _h.Down(Vk.LShift);
        Assert.Equal("CtrlShift", _h.SingleEntry().Text);

        // 片方を離してもすぐには表示を変えない (ピーク保持)
        _h.Up(Vk.LShift);
        Assert.Equal("CtrlShift", _h.SingleEntry().Text);

        // 残りもすぐ離す → 組み合わせ全体が 1 行として残る
        _h.Up(Vk.LControl);
        Assert.Equal("CtrlShift", _h.SingleEntry().Text);
        Assert.Equal(KeyEntryPhase.Holding, _h.SingleEntry().Phase);
    }

    [Fact]
    public void HoldJudge_NarrowsRowAfterDelay()
    {
        _h.Down(Vk.LControl);
        _h.Down(Vk.LShift);
        _h.Up(Vk.LShift);

        // holdJudgeDelay (0.5s) 押し続けたら残りのキーだけの表示へ狭める
        _h.AdvanceSeconds(0.6);
        Assert.Equal("Ctrl", _h.SingleEntry().Text);
        Assert.Single(_h.Entries); // 行は増えない (stepModifierRelease オフ)
    }

    [Fact]
    public void StepModifierRelease_CreatesHistoryRowPerStep()
    {
        _h.Settings.StepModifierRelease = true;
        _h.Down(Vk.LControl);
        _h.Down(Vk.LShift);
        _h.Up(Vk.LShift);
        _h.AdvanceSeconds(0.6);

        // 「CtrlShift」が履歴化され、「Ctrl」が新しい行になる
        Assert.Equal(new[] { "CtrlShift", "Ctrl" }, _h.Texts());
        Assert.Equal(KeyEntryPhase.Holding, _h.Entries[0].Phase);
        Assert.Equal(KeyEntryPhase.Active, _h.Entries[1].Phase);
    }

    // ── ×n 連打カウント ───────────────────────────────────

    [Fact]
    public void RepeatedCtrlTaps_MergeIntoCount()
    {
        _h.Tap(Vk.LControl);
        _h.Tap(Vk.LControl);
        _h.Tap(Vk.LControl);
        var entry = _h.SingleEntry();
        Assert.Equal("Ctrl", entry.Text);
        Assert.Equal(3, entry.Count);
    }

    [Fact]
    public void CtrlTimesThree_ThenCtrlA_RollsBackAndStartsNewRow()
    {
        // ⌘×3 → ⌘A パターン (Mac 版 DEVLOG の差し戻しロジック)
        _h.Tap(Vk.LControl);
        _h.Tap(Vk.LControl);
        _h.Tap(Vk.LControl);
        _h.Down(Vk.LControl); // 4 打目はコンボの始まり (この時点で ×4)
        Assert.Equal(4, _h.SingleEntry().Count);

        _h.Down(A);
        Assert.Equal(2, _h.Entries.Count);
        Assert.Equal("Ctrl", _h.Entries[0].Text);
        Assert.Equal(3, _h.Entries[0].Count); // 1 回ぶん差し戻し
        Assert.Equal(KeyEntryPhase.Holding, _h.Entries[0].Phase); // 履歴化
        Assert.Equal("CtrlA", _h.Entries[1].Text);
        Assert.Equal(KeyEntryPhase.Active, _h.Entries[1].Phase);
    }

    [Fact]
    public void SameComboTwice_MergesToCount()
    {
        _h.Down(Vk.LControl); _h.Down(C); _h.Up(C); _h.Up(Vk.LControl);
        _h.Down(Vk.LControl); _h.Down(C);
        var entry = _h.SingleEntry();
        Assert.Equal("CtrlC", entry.Text);
        Assert.Equal(2, entry.Count);
        _h.Up(C); _h.Up(Vk.LControl);
    }

    [Fact]
    public void MergeDoesNotHappen_WhenAnotherRowIntervened()
    {
        _h.Down(Vk.LControl); _h.Down(C); _h.Up(C); _h.Up(Vk.LControl);
        _h.Tap(Vk.Escape); // 間に別の行
        _h.Down(Vk.LControl); _h.Down(C); _h.Up(C); _h.Up(Vk.LControl);

        Assert.Equal(new[] { "CtrlC", "Esc", "CtrlC" }, _h.Texts());
        Assert.All(_h.Entries, e => Assert.Equal(1, e.Count));
    }

    [Fact]
    public void AutoRepeat_IncrementsComboCount()
    {
        _h.Down(Vk.LControl);
        _h.Down(C);
        _h.Repeat(C);
        _h.Repeat(C);
        Assert.Equal(3, _h.SingleEntry().Count);
        _h.Up(C); _h.Up(Vk.LControl);
    }

    [Fact]
    public void AutoRepeat_DoesNotCountOnTypingRow()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Down(H);
        _h.Repeat(H);
        Assert.Equal(1, _h.SingleEntry().Count);
    }

    [Fact]
    public void AutoRepeat_DoesNotCountImeSwitchKeys()
    {
        _h.Tap(Vk.NonConvert); // 無変換
        _h.Down(Vk.NonConvert);
        _h.Repeat(Vk.NonConvert);
        Assert.Equal(2, _h.SingleEntry().Count); // タップ+押下の 2 回。リピートでは増えない
        _h.Up(Vk.NonConvert);
    }

    // ── タイピング連結 ────────────────────────────────────

    [Fact]
    public void Typing_ConcatenatesWithinWindow()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Tap(H); _h.Tap(E); _h.Tap(L); _h.Tap(L);
        Assert.Equal("HELL", _h.SingleEntry().Text);
        Assert.True(_h.SingleEntry().IsTyping);
    }

    [Fact]
    public void Typing_SplitsAfterWindowExpires()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Tap(H);
        _h.AdvanceSeconds(1.3); // typingAppendWindow (1.2s) 超過
        _h.Tap(E);
        Assert.Equal(new[] { "H", "E" }, _h.Texts());
        // 打ち切られた行は取り残されず解放されている
        Assert.Equal(KeyEntryPhase.Holding, _h.Entries[0].Phase);
    }

    [Fact]
    public void Typing_HiddenWhenShowAllKeysOff()
    {
        _h.Tap(H); _h.Tap(E);
        Assert.Empty(_h.Entries);
    }

    [Fact]
    public void ShiftDuringTyping_DoesNotCreateShiftRow()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Tap(H);
        _h.Down(Vk.LShift); // タイピング連結中の Shift 単独 → 行を出さない
        _h.Down(E); _h.Up(E);
        _h.Up(Vk.LShift);
        Assert.Equal("HE", _h.SingleEntry().Text);
    }

    [Fact]
    public void SpaceDuringTyping_JoinsAsGlyph()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Tap(H); _h.Tap(Vk.Space); _h.Tap(E);
        Assert.Equal("H␣E", _h.SingleEntry().Text);
    }

    [Fact]
    public void SpaceAlone_ShowsAsSpecialKeyWithLabelStyle()
    {
        _h.Tap(Vk.Space); // 連結が生きていない単独スペースは特殊キー表示
        Assert.Equal("Space", _h.SingleEntry().Text);
    }

    [Fact]
    public void DistinguishCase_PreservesActualInput()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Settings.DistinguishCase = true;
        _h.Tap(H);
        _h.Down(Vk.LShift); _h.Tap(E); _h.Up(Vk.LShift);
        Assert.Equal("hE", _h.SingleEntry().Text);
    }

    [Fact]
    public void ShiftTapCountedRow_RolledBackWhenTypingStarts()
    {
        // Shift 連打 (×n) の直後のタイピング: 最後の 1 回はタイピングの一部なので差し戻す
        _h.Settings.ShowAllKeys = true;
        _h.Tap(Vk.LShift);
        _h.Tap(Vk.LShift);
        Assert.Equal(2, _h.SingleEntry().Count);

        _h.Down(Vk.LShift); // ×3 になる
        Assert.Equal(3, _h.SingleEntry().Count);
        _h.Down(E); // Shift+E はタイピング → Shift 行は ×2 に差し戻して履歴化
        Assert.Equal(2, _h.Entries.Count);
        Assert.Equal(2, _h.Entries[0].Count);
        Assert.Equal("E", _h.Entries[1].Text);
        _h.Up(E); _h.Up(Vk.LShift);
    }

    [Fact]
    public void KanaDisplay_ShowsHiraganaWhenImeOn()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Settings.KanaDisplay = true;
        _h.Layout.JapaneseInputMode = true;
        _h.Tap(0x51); // Q → た
        _h.Tap(0x54); // T → か
        Assert.Equal("たか", _h.SingleEntry().Text);
    }

    // ── 矢印キーのまとめ ──────────────────────────────────

    [Fact]
    public void Arrows_SimultaneousPress_JoinsIntoOneRow()
    {
        _h.Down(Vk.Right);
        _h.Down(Vk.Down); // 押しっぱなしのまま追加 → 同時押し
        Assert.Equal("→↓", _h.SingleEntry().Text);
        _h.Up(Vk.Down); _h.Up(Vk.Right);
    }

    [Fact]
    public void Arrows_SequentialTaps_DoNotJoinInSimultaneousMode()
    {
        _h.Tap(Vk.Right);
        _h.Tap(Vk.Down);
        Assert.Equal(new[] { "→", "↓" }, _h.Texts());
    }

    [Fact]
    public void Arrows_SequentialTaps_JoinInConsecutiveMode()
    {
        _h.Settings.ArrowGrouping = ArrowGrouping.Consecutive;
        _h.Tap(Vk.Right);
        _h.Tap(Vk.Right);
        _h.Tap(Vk.Down);
        Assert.Equal("→→↓", _h.SingleEntry().Text);
    }

    [Fact]
    public void Arrows_SameArrowTaps_MergeToCount()
    {
        _h.Tap(Vk.Right);
        _h.Tap(Vk.Right);
        _h.Tap(Vk.Right);
        var entry = _h.SingleEntry();
        Assert.Equal("→", entry.Text);
        Assert.Equal(3, entry.Count);
    }

    [Fact]
    public void Arrows_CountedRow_RolledBackWhenJoiningDifferentArrow()
    {
        // 長押しリピートで「→ ×n」になった行に別の矢印が来たら、
        // 差し戻して新しい組み合わせ行にする (「→ ×4」が「→↓ ×4」に化けないように)
        _h.Down(Vk.Right);
        _h.Repeat(Vk.Right);
        _h.Repeat(Vk.Right); // → ×3
        Assert.Equal(3, _h.SingleEntry().Count);

        _h.Down(Vk.Down); // 押したまま ↓ (同時押し)
        Assert.Equal(2, _h.Entries.Count);
        Assert.Equal("→", _h.Entries[0].Text);
        Assert.Equal(2, _h.Entries[0].Count); // ×3 の最後の 1 回は結合行の始まり
        Assert.Equal(KeyEntryPhase.Holding, _h.Entries[0].Phase);
        Assert.Equal("→↓", _h.Entries[1].Text);
        _h.Up(Vk.Down); _h.Up(Vk.Right);
    }

    [Fact]
    public void Arrows_WithModifier_TreatedAsCombo()
    {
        _h.Down(Vk.LControl);
        _h.Down(Vk.Right);
        Assert.Equal("Ctrl→", _h.SingleEntry().Text);
        _h.Up(Vk.Right); _h.Up(Vk.LControl);
    }

    // ── Caps Lock ────────────────────────────────────────

    [Fact]
    public void CapsLock_FlashesAndMergesOnRepeatTaps()
    {
        _h.Tap(Vk.CapsLock);
        var first = _h.SingleEntry();
        Assert.Equal("CapsLock", first.Text);
        Assert.Equal(KeyEntryPhase.Holding, first.Phase); // 即リリース (フラッシュ表示)

        _h.Tap(Vk.CapsLock);
        var merged = _h.SingleEntry();
        Assert.Equal(2, merged.Count);
        Assert.Equal(KeyEntryPhase.Holding, merged.Phase);
    }

    // ── マウス + キー ─────────────────────────────────────

    [Fact]
    public void CtrlClick_ConvertsModifierRowToClickRow()
    {
        _h.Down(Vk.LControl);
        _h.MouseDown();
        var entry = _h.SingleEntry();
        Assert.Equal("Ctrl«click»", entry.Text);
        Assert.Equal(KeyEntryPhase.Active, entry.Phase);

        _h.MouseUp();
        _h.Up(Vk.LControl);
        Assert.Equal(KeyEntryPhase.Holding, _h.SingleEntry().Phase);
    }

    [Fact]
    public void PlainClick_ShowsNothing()
    {
        _h.MouseDown();
        _h.MouseUp();
        Assert.Empty(_h.Entries);
    }

    [Fact]
    public void RightClick_UsesRightClickToken()
    {
        _h.Down(Vk.LShift);
        _h.MouseDown(button: 1);
        Assert.Equal("Shift«rclick»", _h.SingleEntry().Text);
        _h.MouseUp(button: 1);
        _h.Up(Vk.LShift);
    }

    [Fact]
    public void ClickDisabled_ShowsNothing()
    {
        _h.Settings.ShowClickInKeyDisplay = false;
        _h.Down(Vk.LControl);
        _h.MouseDown();
        Assert.Equal("Ctrl", _h.SingleEntry().Text); // クリック行にはならない
        _h.MouseUp();
        _h.Up(Vk.LControl);
    }

    // ── reconcile / 可視性 ───────────────────────────────

    [Fact]
    public void Reconcile_ReleasesRowWhenKeyUpWasLost()
    {
        _h.Settings.ShowAllKeys = true;
        _h.Down(H);
        Assert.Equal(KeyEntryPhase.Active, _h.SingleEntry().Phase);

        // keyup が届かないままキーが物理的に離された (フォーカス喪失など)
        _h.Probe.PhysicallyDown.Remove(H);
        _h.Machine.ReconcileHeldState();
        Assert.Equal(KeyEntryPhase.Holding, _h.SingleEntry().Phase);
    }

    [Fact]
    public void HiddenOverlay_ProcessesNoInput()
    {
        _h.Settings.OverlayVisible = false;
        _h.Down(Vk.LControl);
        _h.Down(C);
        Assert.Empty(_h.Entries);
        _h.Up(C); _h.Up(Vk.LControl);
    }

    [Fact]
    public void ModifierHeldWhileHidden_StillFormsComboAfterReshow()
    {
        // 非表示中に押した修飾キーは、再表示後のコンボに正しく反映される
        _h.Settings.OverlayVisible = false;
        _h.Down(Vk.LControl);
        _h.Settings.OverlayVisible = true;
        _h.Down(C);
        Assert.Equal("CtrlC", _h.SingleEntry().Text);
        _h.Up(C); _h.Up(Vk.LControl);
    }

    [Fact]
    public void HotCornerSuppressed_ProcessesNoInput()
    {
        _h.Settings.HotCornerSuppressed = true;
        _h.Tap(Vk.Escape);
        Assert.Empty(_h.Entries);
    }

    // ── メディアキー ─────────────────────────────────────

    [Fact]
    public void MediaKeys_ShowAsSpecialKeys()
    {
        _h.Tap(Vk.VolumeUp);
        _h.Tap(Vk.VolumeUp);
        var entry = _h.SingleEntry();
        Assert.Equal("Vol+", entry.Text);
        Assert.Equal(2, entry.Count);
    }
}
