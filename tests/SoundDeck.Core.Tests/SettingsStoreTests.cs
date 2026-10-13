using SoundDeck.Core.Hotkeys;
using SoundDeck.Core.Profiles;
using SoundDeck.Core.Settings;

namespace SoundDeck.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SoundDeckTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void ファイルが無ければ既定値()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.True(settings.General.ShowPeakMeters);
        Assert.Equal(2, settings.Hotkeys.Count);
    }

    [Fact]
    public void 保存して読み戻せる()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings();
        settings.General.ShowOnlyDefaultDevice = true;
        settings.Profiles.Add(new Profile { Name = "ゲーム", DefaultOutputId = "{x}" });
        settings.Hotkeys[0].Modifiers = HotkeyModifiers.Win | HotkeyModifiers.Shift;
        settings.Devices["{x}"] = new DeviceCustomization { DisplayName = "机のスピーカー" };

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(loaded.General.ShowOnlyDefaultDevice);
        Assert.Equal("ゲーム", Assert.Single(loaded.Profiles).Name);
        Assert.Equal(HotkeyModifiers.Win | HotkeyModifiers.Shift, loaded.Hotkeys[0].Modifiers);
        Assert.Equal("机のスピーカー", loaded.Devices["{x}"].DisplayName);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void 壊れたファイルは退避して既定値で起動する()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ これは JSON ではない");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.NotNull(settings.General);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(_dir, "settings.json.broken-*"));
    }

    [Fact]
    public void nullの項目は既定値に戻す()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "profiles": null, "ducking": { "triggerApps": null, "duckLevelPercent": 500 } }""");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Empty(settings.Profiles);
        Assert.Empty(settings.Ducking.TriggerApps);
        Assert.Equal(100, settings.Ducking.DuckLevelPercent);
    }

    [Fact]
    public void ホットキー表示()
    {
        Assert.Equal("Ctrl + Alt + M", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'M').ToString());
        Assert.True(new HotkeyGesture(HotkeyModifiers.None, 0x7C).IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.None, 'M').IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, 0x11).IsValid);
    }
}
