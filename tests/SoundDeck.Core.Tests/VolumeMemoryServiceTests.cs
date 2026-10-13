using SoundDeck.Core.Commands;
using SoundDeck.Core.VolumeMemory;

namespace SoundDeck.Core.Tests;

public sealed class VolumeMemoryServiceTests
{
    private const string Game = @"c:\games\game.exe";

    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void 記録した値を復元対象として返す()
    {
        var service = new VolumeMemoryService(new VolumeMemorySettings(), _time);

        Assert.True(service.Record(Game, "Game", 0.4f, true));
        Assert.True(service.RecordOutput(Game, "Game", "{headphones}"));

        var entry = service.GetRestoreTarget(Game);
        Assert.NotNull(entry);
        Assert.Equal(0.4f, entry.Volume);
        Assert.True(entry.Muted);
        Assert.Equal("{headphones}", entry.OutputDeviceId);
    }

    [Fact]
    public void ほぼ同じ値なら変更なしとみなす()
    {
        var service = new VolumeMemoryService(new VolumeMemorySettings(), _time);
        service.Record(Game, "Game", 0.4f, false);

        Assert.False(service.Record(Game, "Game", 0.401f, false));
    }

    [Fact]
    public void 除外したアプリは記録も復元もしない_既存の記録も消す()
    {
        var service = new VolumeMemoryService(new VolumeMemorySettings(), _time);
        service.Record(Game, "Game", 0.4f, false);

        service.SetExcluded(Game, true);

        Assert.False(service.Record(Game, "Game", 0.2f, false));
        Assert.Null(service.GetRestoreTarget(Game));
        Assert.Empty(service.Settings.Apps);
    }

    [Fact]
    public void システム音と無効時は記録しない()
    {
        var settings = new VolumeMemorySettings();
        var service = new VolumeMemoryService(settings, _time);

        Assert.False(service.Record(AppKeys.SystemSounds, "システム音", 0.2f, false));

        settings.Enabled = false;
        Assert.False(service.Record(Game, "Game", 0.2f, false));
    }

    [Fact]
    public void 出力先を覚えない設定なら出力先は書かない()
    {
        var service = new VolumeMemoryService(new VolumeMemorySettings { RestoreOutputDevice = false }, _time);

        Assert.False(service.RecordOutput(Game, "Game", "{headphones}"));
        service.Apply(new SetAppStateCommand(Game, "Game", 0.5f, null, true, "{headphones}"));

        Assert.Null(service.GetRestoreTarget(Game)!.OutputDeviceId);
        Assert.Equal(0.5f, service.GetRestoreTarget(Game)!.Volume);
    }
}
