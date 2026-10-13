namespace SoundDeck.Core.Rules;

public enum RuleTriggerKind
{
    /// <summary>指定デバイスがつながった（有効になった）。</summary>
    DeviceConnected,

    /// <summary>指定デバイスが外れた（無効になった）。</summary>
    DeviceDisconnected,

    /// <summary>指定アプリが音を出し始めた。</summary>
    AppAudioStarted,
}

public enum RuleActionKind
{
    SetDefaultOutput,
    SetDefaultInput,
    ApplyProfile,
}

/// <summary>「〜したら〜する」の自動切替ルール。</summary>
public sealed class AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public RuleTriggerKind Trigger { get; set; }

    /// <summary>デバイス系トリガーの対象デバイス ID。</summary>
    public string? TriggerDeviceId { get; set; }

    /// <summary>アプリ系トリガーの対象アプリキー。</summary>
    public string? TriggerAppKey { get; set; }

    public RuleActionKind Action { get; set; }

    public string? ActionDeviceId { get; set; }

    public Guid? ActionProfileId { get; set; }
}

public abstract record AudioEvent;

public sealed record DeviceConnectedEvent(string DeviceId) : AudioEvent;

public sealed record DeviceDisconnectedEvent(string DeviceId) : AudioEvent;

public sealed record AppAudioStartedEvent(string AppKey) : AudioEvent;

/// <summary>
/// 起きた出来事に合うルールを選ぶ。
/// 抜き差しの瞬断や再生の細かい開始・停止で何度も発火しないよう、同じルールは一定時間あけてから再発火させる。
/// </summary>
public sealed class RuleEngine(TimeProvider time)
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, DateTimeOffset> _lastFired = [];

    public TimeSpan Cooldown { get; init; } = DefaultCooldown;

    public IReadOnlyList<AutomationRule> Match(IEnumerable<AutomationRule> rules, AudioEvent audioEvent)
    {
        var now = time.GetUtcNow();
        var matched = new List<AutomationRule>();
        foreach (var rule in rules)
        {
            if (!rule.Enabled || !IsMatch(rule, audioEvent))
            {
                continue;
            }

            if (_lastFired.TryGetValue(rule.Id, out var last) && now - last < Cooldown)
            {
                continue;
            }

            _lastFired[rule.Id] = now;
            matched.Add(rule);
        }

        return matched;
    }

    private static bool IsMatch(AutomationRule rule, AudioEvent audioEvent) => (rule.Trigger, audioEvent) switch
    {
        (RuleTriggerKind.DeviceConnected, DeviceConnectedEvent e) => SameId(rule.TriggerDeviceId, e.DeviceId),
        (RuleTriggerKind.DeviceDisconnected, DeviceDisconnectedEvent e) => SameId(rule.TriggerDeviceId, e.DeviceId),
        (RuleTriggerKind.AppAudioStarted, AppAudioStartedEvent e) => SameId(rule.TriggerAppKey, e.AppKey),
        _ => false,
    };

    private static bool SameId(string? expected, string actual) =>
        expected is not null && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
