using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Input;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Settings;
using KeyDisp.Core.StateMachine;

namespace KeyDisp.Core.Tests.TestSupport;

/// <summary>US 配列風のフェイクレイアウト。英字は a-z、数字はそのまま返す。</summary>
public sealed class FakeKeyboardLayout : IKeyboardLayout
{
    public bool JapaneseInputMode { get; set; }

    public string? CharacterFor(int vk, bool shifted, bool capsLock)
    {
        if (vk is >= Vk.A and <= Vk.Z)
        {
            var upper = shifted ^ capsLock;
            var ch = (char)('a' + (vk - Vk.A));
            return upper ? char.ToUpperInvariant(ch).ToString() : ch.ToString();
        }
        if (vk is >= Vk.D0 and <= Vk.D9)
        {
            if (!shifted) return ((char)('0' + (vk - Vk.D0))).ToString();
            // US 配列の Shift+数字
            return "!@#$%^&*()"[vk == Vk.D0 ? 9 : vk - Vk.D0 - 1].ToString();
        }
        return null;
    }

    public bool IsJapaneseInputMode() => JapaneseInputMode;
}

/// <summary>ハーネスが管理する「物理的に押しているキー集合」をそのまま返すプローブ。</summary>
public sealed class FakeInputStateProbe : IInputStateProbe
{
    public HashSet<int> PhysicallyDown { get; } = new();
    public bool CapsLockOn { get; set; }

    public bool IsKeyDown(int vk) => PhysicallyDown.Contains(vk);

    public ModifierKeys RealModifiers
    {
        get
        {
            var flags = ModifierKeys.None;
            foreach (var vk in PhysicallyDown) flags |= KeyFormatter.ModifierOf(vk);
            return flags;
        }
    }

    public bool IsCapsLockOn => CapsLockOn;
}

/// <summary>
/// 状態機械のシナリオテスト用ハーネス。物理押下の追跡・時間・イベント送出をまとめる。
/// </summary>
public sealed class StateMachineHarness
{
    public AppSettings Settings { get; } = new();
    public VirtualScheduler Clock { get; } = new();
    public FakeKeyboardLayout Layout { get; } = new();
    public FakeInputStateProbe Probe { get; } = new();
    public KeyDisplayModel Model { get; }
    public KeyFormatter Formatter { get; }
    public KeyStateMachine Machine { get; }

    public StateMachineHarness(ITypingLayout? typingLayout = null)
    {
        Model = new KeyDisplayModel(Settings, Clock);
        Formatter = new KeyFormatter(Settings, Layout);
        Machine = new KeyStateMachine(
            Model, Settings, Formatter, Clock, Probe, typingLayout ?? new UnlimitedTypingLayout());
    }

    public void Down(int vk)
    {
        Probe.PhysicallyDown.Add(vk);
        Machine.HandleKey(vk, isDown: true);
    }

    /// <summary>押しっぱなしの autorepeat (フックが合成する isRepeat 付き keydown)。</summary>
    public void Repeat(int vk) => Machine.HandleKey(vk, isDown: true, isRepeat: true);

    public void Up(int vk)
    {
        Probe.PhysicallyDown.Remove(vk);
        Machine.HandleKey(vk, isDown: false);
    }

    public void Tap(int vk)
    {
        Down(vk);
        Up(vk);
    }

    public void MouseDown(int button = 0) => Machine.HandleMouseButton(button, isDown: true);
    public void MouseUp(int button = 0) => Machine.HandleMouseButton(button, isDown: false);

    public void AdvanceSeconds(double seconds) => Clock.AdvanceSeconds(seconds);

    // ── 検証補助 ─────────────────────────────────────────

    public IReadOnlyList<KeyEntry> Entries => Model.Entries;

    public KeyEntry SingleEntry() => Assert.Single(Model.Entries);

    public string[] Texts() => Model.Entries.Select(e => e.Text).ToArray();
}
