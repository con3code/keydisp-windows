using KeyDisp.Core.Display;
using KeyDisp.Core.Settings;
using KeyDisp.Core.Tests.TestSupport;

namespace KeyDisp.Core.Tests.Display;

/// <summary>
/// 行ライフサイクル (active→holding→fading→削除) の検証。
/// 既定値: holdDuration=1.5s, fadeDuration=0.8s → 削除は release から 1.5+0.8+0.1=2.4s 後。
/// </summary>
public class KeyDisplayModelTests
{
    private readonly AppSettings _settings = new();
    private readonly VirtualScheduler _clock = new();
    private readonly KeyDisplayModel _model;

    public KeyDisplayModelTests()
    {
        _model = new KeyDisplayModel(_settings, _clock);
    }

    private KeyEntry Single() => Assert.Single(_model.Entries);

    [Fact]
    public void Release_TransitionsThroughHoldingAndFadingToRemoval()
    {
        var id = _model.Begin(new[] { "Ctrl", "C" }, isTyping: false);
        Assert.Equal(KeyEntryPhase.Active, Single().Phase);

        _model.Release(id);
        Assert.Equal(KeyEntryPhase.Holding, Single().Phase);

        _clock.AdvanceSeconds(1.4); // hold 1.5s の手前
        Assert.Equal(KeyEntryPhase.Holding, Single().Phase);

        _clock.AdvanceSeconds(0.2); // hold を超えた
        Assert.Equal(KeyEntryPhase.Fading, Single().Phase);

        _clock.AdvanceSeconds(0.9); // 1.5+0.8+0.1=2.4s を超えた
        Assert.Empty(_model.Entries);
    }

    [Fact]
    public void Release_OnNonActiveEntry_DoesNothing()
    {
        var id = _model.Begin(new[] { "A" }, isTyping: true);
        _model.Release(id);
        _clock.AdvanceSeconds(1.6);
        Assert.Equal(KeyEntryPhase.Fading, Single().Phase);

        // fading の行を再度 release しても状態は変わらない
        _model.Release(id);
        Assert.Equal(KeyEntryPhase.Fading, Single().Phase);
    }

    [Fact]
    public void Append_RevivesHoldingEntry_AndCancelsScheduledFade()
    {
        var id = _model.Begin(new[] { "H" }, isTyping: true);
        _model.Release(id);
        _clock.AdvanceSeconds(1.0);

        Assert.True(_model.Append(id, "E"));
        Assert.Equal(KeyEntryPhase.Active, Single().Phase);
        Assert.Equal("HE", Single().Text);

        // 予定されていたフェードは取り消されているので時間が経っても消えない
        _clock.AdvanceSeconds(10);
        Assert.Equal(KeyEntryPhase.Active, Single().Phase);
    }

    [Fact]
    public void Append_OnFadingEntry_ReturnsFalse()
    {
        var id = _model.Begin(new[] { "H" }, isTyping: true);
        _model.Release(id);
        _clock.AdvanceSeconds(1.6); // fading へ
        Assert.False(_model.Append(id, "E"));
    }

    [Fact]
    public void Update_ResetsCountAndKeepsPhase()
    {
        var id = _model.Begin(new[] { "Ctrl" }, isTyping: false);
        _model.Increment(id);
        Assert.Equal(2, Single().Count);

        _model.Update(id, new[] { "Ctrl", "C" });
        Assert.Equal(1, Single().Count);
        Assert.Equal("CtrlC", Single().Text);
        Assert.Equal(KeyEntryPhase.Active, Single().Phase);
    }

    [Fact]
    public void Increment_RevivesEntryAndCancelsFade()
    {
        var id = _model.Begin(new[] { "Esc" }, isTyping: false);
        _model.Release(id);
        _clock.AdvanceSeconds(1.0);

        _model.Increment(id);
        Assert.Equal(2, Single().Count);
        Assert.Equal(KeyEntryPhase.Active, Single().Phase);
        _clock.AdvanceSeconds(10);
        Assert.Single(_model.Entries); // フェード予定は取り消し済み
    }

    [Fact]
    public void Decrement_DoesNotGoBelowOne()
    {
        var id = _model.Begin(new[] { "Ctrl" }, isTyping: false);
        _model.Decrement(id);
        Assert.Equal(1, Single().Count);
    }

    [Fact]
    public void TrimRows_RemovesOldestBeyondMaxRows()
    {
        _settings.MaxRows = 2;
        _model.Begin(new[] { "1" }, false);
        _model.Begin(new[] { "2" }, false);
        _model.Begin(new[] { "3" }, false);

        Assert.Equal(2, _model.Entries.Count);
        Assert.Equal("2", _model.Entries[0].Text); // 古い方 (先頭) から削除
        Assert.Equal("3", _model.Entries[1].Text);
    }

    [Fact]
    public void Freeze_CancelsPendingRemovals_AndRestoresFadingToHolding()
    {
        var id = _model.Begin(new[] { "A" }, isTyping: true);
        _model.Release(id);
        _clock.AdvanceSeconds(1.6); // fading へ
        Assert.Equal(KeyEntryPhase.Fading, Single().Phase);

        _model.SetFrozen(true);
        Assert.Equal(KeyEntryPhase.Holding, Single().Phase);

        // 凍結中は時間が経っても消えない
        _clock.AdvanceSeconds(60);
        Assert.Single(_model.Entries);

        // 解除するとフェードが予約し直され、hold+fade+0.1 後に消える
        _model.SetFrozen(false);
        _clock.AdvanceSeconds(2.5);
        Assert.Empty(_model.Entries);
    }

    [Fact]
    public void Freeze_MultipleReasons_UnfreezesOnlyWhenAllCleared()
    {
        var id = _model.Begin(new[] { "A" }, isTyping: true);
        _model.Release(id);

        _model.SetFreeze(FreezeReason.TopEdge, true);
        _model.SetFreeze(FreezeReason.Dragging, true);
        _model.SetFreeze(FreezeReason.TopEdge, false);
        Assert.True(_model.IsFrozen);

        _clock.AdvanceSeconds(60);
        Assert.Single(_model.Entries);

        _model.SetFreeze(FreezeReason.Dragging, false);
        Assert.False(_model.IsFrozen);
        _clock.AdvanceSeconds(2.5);
        Assert.Empty(_model.Entries);
    }

    [Fact]
    public void ReleaseDuringFreeze_DoesNotScheduleFade_UntilUnfrozen()
    {
        var id = _model.Begin(new[] { "A" }, isTyping: true);
        _model.SetFrozen(true);
        _model.Release(id); // 凍結中の release: holding にはなるがフェード予約なし
        Assert.Equal(KeyEntryPhase.Holding, Single().Phase);

        _clock.AdvanceSeconds(60);
        Assert.Single(_model.Entries);

        _model.SetFrozen(false);
        _clock.AdvanceSeconds(2.5);
        Assert.Empty(_model.Entries);
    }

    [Fact]
    public void ReleaseOtherTypingRows_ReleasesOnlyOtherActiveTypingRows()
    {
        var typing1 = _model.Begin(new[] { "A" }, isTyping: true);
        var combo = _model.Begin(new[] { "Ctrl", "C" }, isTyping: false);
        var typing2 = _model.Begin(new[] { "B" }, isTyping: true);

        _model.ReleaseOtherTypingRows(exceptId: typing2);

        Assert.Equal(KeyEntryPhase.Holding, _model.PhaseOf(typing1));
        Assert.Equal(KeyEntryPhase.Active, _model.PhaseOf(combo));
        Assert.Equal(KeyEntryPhase.Active, _model.PhaseOf(typing2));
    }

    [Fact]
    public void Flash_BeginsAndImmediatelyReleases()
    {
        _model.Flash(new[] { "CapsLock" });
        Assert.Equal(KeyEntryPhase.Holding, Single().Phase);
        _clock.AdvanceSeconds(2.5);
        Assert.Empty(_model.Entries);
    }

    [Fact]
    public void ClearAll_RemovesEverythingAndCancelsWork()
    {
        var id = _model.Begin(new[] { "A" }, isTyping: true);
        _model.Release(id);
        _model.ClearAll();
        Assert.Empty(_model.Entries);
        _clock.AdvanceSeconds(10); // 取り消し済みの予定が例外を出さないこと
        Assert.Empty(_model.Entries);
    }
}
