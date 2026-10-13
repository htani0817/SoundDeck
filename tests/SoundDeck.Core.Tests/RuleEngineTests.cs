using SoundDeck.Core.Rules;

namespace SoundDeck.Core.Tests;

public sealed class RuleEngineTests
{
    private readonly ManualTimeProvider _time = new();

    private static AutomationRule HeadphonesRule() => new()
    {
        Name = "ヘッドホンをつないだら切り替え",
        Trigger = RuleTriggerKind.DeviceConnected,
        TriggerDeviceId = "{HEADPHONES}",
        Action = RuleActionKind.SetDefaultOutput,
        ActionDeviceId = "{headphones}",
    };

    [Fact]
    public void 条件に合うルールだけ選ぶ_IDの大文字小文字は区別しない()
    {
        var engine = new RuleEngine(_time);
        var rule = HeadphonesRule();
        var other = new AutomationRule { Trigger = RuleTriggerKind.DeviceDisconnected, TriggerDeviceId = "{headphones}" };

        var matched = engine.Match([rule, other], new DeviceConnectedEvent("{headphones}"));

        Assert.Equal([rule], matched);
    }

    [Fact]
    public void 無効なルールは選ばない()
    {
        var engine = new RuleEngine(_time);
        var rule = HeadphonesRule();
        rule.Enabled = false;

        Assert.Empty(engine.Match([rule], new DeviceConnectedEvent("{headphones}")));
    }

    [Fact]
    public void 連続した出来事では待ち時間内に再発火しない()
    {
        var engine = new RuleEngine(_time);
        var rule = HeadphonesRule();

        Assert.Single(engine.Match([rule], new DeviceConnectedEvent("{headphones}")));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(engine.Match([rule], new DeviceConnectedEvent("{headphones}")));

        _time.Advance(RuleEngine.DefaultCooldown);
        Assert.Single(engine.Match([rule], new DeviceConnectedEvent("{headphones}")));
    }

    [Fact]
    public void アプリの再生開始で発火する()
    {
        var engine = new RuleEngine(_time);
        var rule = new AutomationRule
        {
            Trigger = RuleTriggerKind.AppAudioStarted,
            TriggerAppKey = @"c:\apps\discord.exe",
            Action = RuleActionKind.ApplyProfile,
            ActionProfileId = Guid.NewGuid(),
        };

        Assert.Single(engine.Match([rule], new AppAudioStartedEvent(@"c:\apps\discord.exe")));
        Assert.Empty(engine.Match([rule], new DeviceConnectedEvent(@"c:\apps\discord.exe")));
    }
}
