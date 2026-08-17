using KeyDisp.Core.Screens;
using KeyDisp.Core.Settings;
using KeyDisp.Core.Tests.TestSupport;

namespace KeyDisp.Core.Tests.Screens;

public class ScreenProfileStoreTests
{
    private readonly AppSettings _settings = new();
    private readonly VirtualScheduler _clock = new();
    private string? _currentScreen = "screen-A";
    private readonly ScreenProfileStore _store;

    public ScreenProfileStoreTests()
    {
        _store = new ScreenProfileStore(_settings, _clock, () => _currentScreen);
    }

    [Fact]
    public void SettingChange_RemembersProfileAfterDebounce()
    {
        _settings.DisplayScale = 2.0;
        Assert.Empty(_store.Profiles); // デバウンス中はまだ記憶しない
        _clock.AdvanceSeconds(0.2);
        Assert.Equal(2.0, _store.Profiles["screen-A"].DisplayScale);
    }

    [Fact]
    public void RapidChanges_CoalesceIntoOneRemember()
    {
        var changed = 0;
        _store.Changed += () => changed++;
        _settings.DisplayScale = 2.0;
        _clock.AdvanceSeconds(0.05);
        _settings.MaxRows = 6;
        _clock.AdvanceSeconds(0.05);
        _settings.StackFromTop = true;
        _clock.AdvanceSeconds(0.2);
        Assert.Equal(1, changed);
        var doc = _store.Profiles["screen-A"];
        Assert.Equal(2.0, doc.DisplayScale);
        Assert.Equal(6, doc.MaxRows);
        Assert.True(doc.StackFromTop);
    }

    [Fact]
    public void NonProfileSetting_DoesNotTriggerRemember()
    {
        _settings.HoldDuration = 3.0; // プロファイル対象外
        _clock.AdvanceSeconds(0.5);
        Assert.Empty(_store.Profiles);
    }

    [Fact]
    public void Adopt_AppliesStoredProfile()
    {
        _settings.DisplayScale = 3.0;
        _settings.KeyStyle = KeyStyle.Simple;
        _settings.HiddenOnCurrentScreen = true;
        _store.RememberProfile("screen-B");

        _settings.DisplayScale = 1.0;
        _settings.KeyStyle = KeyStyle.Keycap;
        _settings.HiddenOnCurrentScreen = false;
        _clock.AdvanceSeconds(0.2); // screen-A に現状を記憶させておく

        _store.Adopt("screen-B");
        Assert.Equal(3.0, _settings.DisplayScale);
        Assert.Equal(KeyStyle.Simple, _settings.KeyStyle);
        Assert.True(_settings.HiddenOnCurrentScreen);
    }

    [Fact]
    public void Adopt_DoesNotRetriggerRemember()
    {
        _settings.DisplayScale = 3.0;
        _store.RememberProfile("screen-B");
        _settings.DisplayScale = 1.0;
        _clock.AdvanceSeconds(0.2); // screen-A に記憶

        _currentScreen = "screen-B";
        _store.Adopt("screen-B"); // 適用による設定変更は記憶を起こさない (再入ガード)
        _clock.AdvanceSeconds(0.5);
        // screen-B のプロファイルが適用時の値のまま (1.0 で上書きされていない)
        Assert.Equal(3.0, _store.Profiles["screen-B"].DisplayScale);
    }

    [Fact]
    public void Adopt_UnknownScreen_DefaultsToVisible()
    {
        _settings.HiddenOnCurrentScreen = true;
        _store.Adopt("unknown-screen");
        Assert.False(_settings.HiddenOnCurrentScreen);
    }

    [Fact]
    public void StoredFrame_RequiresIntersectionWithScreen()
    {
        _store.RememberFrame("screen-A", new RectD(100, 100, 600, 400));
        var bounds = new RectD(0, 0, 1920, 1080);
        Assert.Equal(new RectD(100, 100, 600, 400), _store.StoredFrame("screen-A", bounds));
        // 画面と交差しない記憶は無効 (モニタ構成が変わった場合)
        var farBounds = new RectD(5000, 0, 1920, 1080);
        Assert.Null(_store.StoredFrame("screen-A", farBounds));
    }

    [Fact]
    public void RestoreHiddenFlag_ReadsOnlyHidden()
    {
        _settings.HiddenOnCurrentScreen = true;
        _settings.DisplayScale = 3.0;
        _store.RememberProfile("screen-A");
        _settings.HiddenOnCurrentScreen = false;
        _settings.DisplayScale = 1.0;
        _clock.AdvanceSeconds(0.2);
        _store.RememberProfile("screen-A"); // 最新状態で上書き
        _settings.HiddenOnCurrentScreen = true;
        _clock.AdvanceSeconds(0.2);

        // hidden=true が記憶されている状態で、他の値は触らず hidden だけ復元
        _settings.HiddenOnCurrentScreen = false;
        _settings.DisplayScale = 2.5;
        _store.RestoreHiddenFlag("screen-A");
        Assert.True(_settings.HiddenOnCurrentScreen);
        Assert.Equal(2.5, _settings.DisplayScale); // 変わらない
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        _store.RememberProfile("screen-A");
        _store.RememberFrame("screen-A", new RectD(0, 0, 100, 100));
        _store.Reset();
        Assert.Empty(_store.Profiles);
    }
}
