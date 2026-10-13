using SoundDeck.Core.Ducking;

namespace SoundDeck.Core.Tests;

public sealed class DuckingControllerTests
{
    private const string Discord = @"c:\apps\discord.exe";
    private const string Game = @"c:\games\game.exe";
    private const string Music = @"c:\apps\music.exe";

    private readonly ManualTimeProvider _time = new();
    private readonly DuckingSettings _settings = new()
    {
        Enabled = true,
        TriggerApps = [Discord],
        DuckLevelPercent = 50,
        Threshold = 0.05f,
        ReleaseDelayMs = 1000,
    };

    [Fact]
    public void トリガーが鳴るとほかのアプリだけ下がる()
    {
        var controller = new DuckingController(_time);

        var changes = controller.Update(_settings, [
            new("d", Discord, 0.3f, 1f),
            new("g", Game, 0.5f, 0.8f),
            new("m", Music, 0f, 0.6f),
        ]);

        Assert.True(controller.IsDucked);
        Assert.Equal([new VolumeChange("g", 0.4f), new VolumeChange("m", 0.3f)], changes);
    }

    [Fact]
    public void しきい値未満では下げない()
    {
        var controller = new DuckingController(_time);

        var changes = controller.Update(_settings, [new("d", Discord, 0.01f, 1f), new("g", Game, 0.5f, 0.8f)]);

        Assert.False(controller.IsDucked);
        Assert.Empty(changes);
    }

    [Fact]
    public void 止まってから待ち時間が過ぎると元に戻す()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f)]);

        // 止まった直後はまだ戻さない
        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Empty(controller.Update(_settings, [new("d", Discord, 0f, 1f), new("g", Game, 0.5f, 0.4f)]));
        Assert.True(controller.IsDucked);

        _time.Advance(TimeSpan.FromMilliseconds(600));
        var changes = controller.Update(_settings, [new("d", Discord, 0f, 1f), new("g", Game, 0.5f, 0.4f)]);

        Assert.False(controller.IsDucked);
        Assert.Equal([new VolumeChange("g", 0.8f)], changes);
    }

    [Fact]
    public void 下げ中に二重に下げない()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f)]);

        var changes = controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.4f)]);

        Assert.Empty(changes);
    }

    [Fact]
    public void 下げ中に現れたセッションも下げる()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f)]);

        var changes = controller.Update(_settings, [
            new("d", Discord, 0.3f, 1f),
            new("g", Game, 0.5f, 0.4f),
            new("m", Music, 0.2f, 1f),
        ]);

        Assert.Equal([new VolumeChange("m", 0.5f)], changes);
    }

    [Fact]
    public void ユーザーが触ったセッションは戻さない()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f), new("m", Music, 0.1f, 0.6f)]);
        controller.ForgetSession("m");

        _time.Advance(TimeSpan.FromSeconds(2));
        var changes = controller.Update(_settings, [new("d", Discord, 0f, 1f), new("g", Game, 0.5f, 0.4f), new("m", Music, 0.1f, 0.9f)]);

        Assert.Equal([new VolumeChange("g", 0.8f)], changes);
    }

    [Fact]
    public void 無効にしたらすぐ戻す()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f)]);

        _settings.Enabled = false;
        var changes = controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.4f)]);

        Assert.False(controller.IsDucked);
        Assert.Equal([new VolumeChange("g", 0.8f)], changes);
    }

    [Fact]
    public void 消えたセッションは戻さない()
    {
        var controller = new DuckingController(_time);
        controller.Update(_settings, [new("d", Discord, 0.3f, 1f), new("g", Game, 0.5f, 0.8f)]);

        _time.Advance(TimeSpan.FromSeconds(2));
        var changes = controller.Update(_settings, [new("d", Discord, 0f, 1f)]);

        Assert.Empty(changes);
        Assert.False(controller.IsDucked);
    }
}
