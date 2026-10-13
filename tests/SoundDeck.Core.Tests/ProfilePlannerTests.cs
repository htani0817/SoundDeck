using SoundDeck.Core.Commands;
using SoundDeck.Core.Profiles;

namespace SoundDeck.Core.Tests;

public sealed class ProfilePlannerTests
{
    private const string Speakers = "{speakers}";
    private const string Headphones = "{headphones}";
    private const string Mic = "{mic}";
    private const string Game = @"c:\games\game.exe";
    private const string Music = @"c:\apps\music.exe";

    private static AudioSnapshot Current() => new(
        Speakers,
        Mic,
        [
            new(Speakers, DeviceFlow.Render, 0.5f, false),
            new(Headphones, DeviceFlow.Render, 0.3f, false),
            new(Mic, DeviceFlow.Capture, 0.8f, false),
        ],
        [
            new(Game, "Game", 1f, false, null),
            new(AppKeys.SystemSounds, "システム音", 0.5f, false, null),
        ]);

    [Fact]
    public void 今の状態を保存するとシステム音は含めない()
    {
        var profile = ProfilePlanner.Capture("いつもの", Current());

        Assert.Equal("いつもの", profile.Name);
        Assert.Equal(Speakers, profile.DefaultOutputId);
        Assert.Equal(3, profile.Devices.Count);
        Assert.Single(profile.Apps);
        Assert.Equal(Game, profile.Apps[0].AppKey);
    }

    [Fact]
    public void 保存した直後に当てても何もしない()
    {
        var current = Current();
        var profile = ProfilePlanner.Capture("同じ", current);

        Assert.Empty(ProfilePlanner.Plan(profile, current));
    }

    [Fact]
    public void 差分だけを指示にする()
    {
        var profile = new Profile
        {
            DefaultOutputId = Headphones,
            Devices = [new() { DeviceId = Headphones, Volume = 0.3f, Muted = true }],
            Apps = [new() { AppKey = Game, DisplayName = "Game", Volume = 0.4f, Muted = false, OutputDeviceId = Headphones }],
        };

        var commands = ProfilePlanner.Plan(profile, Current());

        Assert.Equal(
            [
                new SetDefaultDeviceCommand(Headphones, DeviceFlow.Render),
                new SetDeviceVolumeCommand(Headphones, null, true),
                new SetAppStateCommand(Game, "Game", 0.4f, null, true, Headphones),
            ],
            commands);
    }

    [Fact]
    public void つながっていないデバイスは飛ばす()
    {
        var profile = new Profile
        {
            DefaultOutputId = "{gone}",
            Devices = [new() { DeviceId = "{gone}", Volume = 0.1f }],
            Apps = [new() { AppKey = Game, DisplayName = "Game", Volume = 1f, OutputDeviceId = "{gone}" }],
        };

        Assert.Empty(ProfilePlanner.Plan(profile, Current()));
    }

    [Fact]
    public void 録音デバイスを出力の既定にはしない()
    {
        var profile = new Profile { DefaultOutputId = Mic };

        Assert.Empty(ProfilePlanner.Plan(profile, Current()));
    }

    [Fact]
    public void 起動していないアプリも次回用の指示を出す()
    {
        var profile = new Profile
        {
            Apps = [new() { AppKey = Music, DisplayName = "Music", Volume = 0.2f, Muted = false, OutputDeviceId = Headphones }],
        };

        var command = Assert.Single(ProfilePlanner.Plan(profile, Current()));

        Assert.Equal(new SetAppStateCommand(Music, "Music", 0.2f, false, true, Headphones), command);
    }
}
