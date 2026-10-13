namespace SoundDeck.Core.Tests;

/// <summary>テスト用の手動で進める時計。</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
