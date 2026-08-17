using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Tests.Settings;

public class SettingsDocumentTests
{
    [Fact]
    public void Roundtrip_PreservesAllValues()
    {
        var original = new AppSettings
        {
            DisplayScale = 2.5,
            HoldDuration = 3.0,
            MaxRows = 6,
            StackFromTop = true,
            ShowAllKeys = true,
            KeyStyle = KeyStyle.CustomImage,
            OSLabelStyle = OSLabelStyle.Both,
            TextColorHex = "#123456",
            KanaDisplay = true,
            ArrowGrouping = ArrowGrouping.Consecutive,
            HotKeyVk = 0x42,
            HotKeyModifiers = 0x0003,
            LaunchAtLogin = true,
        };

        var json = SettingsDocument.From(original).ToJson();
        var restored = new AppSettings();
        SettingsDocument.FromJson(json).Apply(restored);

        Assert.Equal(2.5, restored.DisplayScale);
        Assert.Equal(3.0, restored.HoldDuration);
        Assert.Equal(6, restored.MaxRows);
        Assert.True(restored.StackFromTop);
        Assert.True(restored.ShowAllKeys);
        Assert.Equal(KeyStyle.CustomImage, restored.KeyStyle);
        Assert.Equal(OSLabelStyle.Both, restored.OSLabelStyle);
        Assert.Equal("#123456", restored.TextColorHex);
        Assert.True(restored.KanaDisplay);
        Assert.Equal(ArrowGrouping.Consecutive, restored.ArrowGrouping);
        Assert.Equal(0x42, restored.HotKeyVk);
        Assert.Equal(0x0003, restored.HotKeyModifiers);
        Assert.True(restored.LaunchAtLogin);
    }

    [Fact]
    public void Json_UsesCamelCaseMacCompatibleKeys()
    {
        var json = SettingsDocument.From(new AppSettings()).ToJson();
        // Mac 版 UserDefaults のキー名を踏襲していること (docs/SPEC.md §9)
        Assert.Contains("\"holdDuration\"", json);
        Assert.Contains("\"fadeDuration\"", json);
        Assert.Contains("\"stackFromTop\"", json);
        Assert.Contains("\"countRepeats\"", json);
        Assert.Contains("\"osLabelStyle\"", json);
        Assert.Contains("\"version\"", json);
    }

    [Fact]
    public void MissingKeys_FallBackToDefaults()
    {
        var restored = new AppSettings();
        SettingsDocument.FromJson("""{ "version": 1, "displayScale": 3.0 }""").Apply(restored);

        Assert.Equal(3.0, restored.DisplayScale);
        Assert.Equal(1.5, restored.HoldDuration);       // 既定値のまま
        Assert.Equal(KeyStyle.Keycap, restored.KeyStyle);
        Assert.True(restored.CountRepeats);
    }

    [Fact]
    public void UnknownKeys_AreIgnored()
    {
        var doc = SettingsDocument.FromJson("""{ "version": 1, "someFutureSetting": true }""");
        var restored = new AppSettings();
        doc.Apply(restored); // 例外にならない
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public void EmptyOrBrokenJson_YieldsDefaults()
    {
        var doc = SettingsDocument.FromJson("{}");
        var restored = new AppSettings();
        doc.Apply(restored);
        Assert.Equal(1.0, restored.DisplayScale);
        Assert.False(restored.ShowAllKeys);
    }

    [Fact]
    public void DisplayProfiles_Roundtrip()
    {
        var doc = new SettingsDocument
        {
            DisplayProfiles = new Dictionary<string, ScreenProfileDocument>
            {
                ["MONITOR\\ABC123"] = new ScreenProfileDocument
                {
                    Frame = new double[] { 100, 200, 620, 440 },
                    Style = 1,
                    DisplayScale = 1.5,
                    Hidden = true,
                },
            },
        };
        var restored = SettingsDocument.FromJson(doc.ToJson());
        var profile = Assert.Single(restored.DisplayProfiles!).Value;
        Assert.Equal(new double[] { 100, 200, 620, 440 }, profile.Frame);
        Assert.Equal(1, profile.Style);
        Assert.Equal(1.5, profile.DisplayScale);
        Assert.True(profile.Hidden);
    }
}
