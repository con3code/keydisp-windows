using KeyDisp.Core.Display;

namespace KeyDisp.Core.Tests.Display;

public class KeyEntryTests
{
    [Fact]
    public void Text_JoinsTokensWithoutSeparator()
    {
        var entry = new KeyEntry(new[] { "Ctrl", "Shift", "S" }, isTyping: false, KeyEntryPhase.Active);
        Assert.Equal("CtrlShiftS", entry.Text);
    }

    [Fact]
    public void NewEntry_StartsWithCountOne()
    {
        var entry = new KeyEntry(new[] { "A" }, isTyping: true, KeyEntryPhase.Active);
        Assert.Equal(1, entry.Count);
        Assert.Equal(KeyEntryPhase.Active, entry.Phase);
    }

    [Fact]
    public void ReplaceTokens_OverwritesAllTokens()
    {
        var entry = new KeyEntry(new[] { "Ctrl" }, isTyping: false, KeyEntryPhase.Active);
        entry.ReplaceTokens(new[] { "Ctrl", "C" });
        Assert.Equal(new[] { "Ctrl", "C" }, entry.Tokens);
    }
}
