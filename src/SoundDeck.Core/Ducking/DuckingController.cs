namespace SoundDeck.Core.Ducking;

public sealed class DuckingSettings
{
    public bool Enabled { get; set; }

    /// <summary>このアプリが音を出している間、ほかのアプリの音量を下げる（アプリキーの一覧）。</summary>
    public List<string> TriggerApps { get; set; } = [];

    /// <summary>下げている間のほかのアプリの音量（元の音量に対する %）。</summary>
    public int DuckLevelPercent { get; set; } = 30;

    /// <summary>この値以上のピークを「音を出している」とみなす（0〜1）。</summary>
    public float Threshold { get; set; } = 0.01f;

    /// <summary>トリガー側の音が止まってから元に戻すまでの待ち時間。</summary>
    public int ReleaseDelayMs { get; set; } = 1500;
}

/// <summary>セッション 1 つ分の今の値。</summary>
public readonly record struct SessionLevel(string SessionId, string AppKey, float Peak, float Volume);

/// <summary>セッションへ設定する音量。</summary>
public readonly record struct VolumeChange(string SessionId, float Volume);

/// <summary>
/// ダッキングの状態を持つ。定期的に <see cref="Update"/> を呼ぶと、変えるべき音量を返す。
/// 実際に音量を変えるのは呼び出し側。
/// </summary>
public sealed class DuckingController(TimeProvider time)
{
    // 下げたセッションの元の音量
    private readonly Dictionary<string, float> _originals = [];
    private DateTimeOffset _lastTriggerActivity;

    public bool IsDucked { get; private set; }

    public bool IsDuckedSession(string sessionId) => _originals.ContainsKey(sessionId);

    /// <summary>ユーザーが下げ中のセッションの音量を自分で変えたら、そのセッションは戻さない。</summary>
    public void ForgetSession(string sessionId) => _originals.Remove(sessionId);

    public IReadOnlyList<VolumeChange> Update(DuckingSettings settings, IReadOnlyList<SessionLevel> sessions)
    {
        if (!settings.Enabled || settings.TriggerApps.Count == 0)
        {
            return IsDucked ? Restore(sessions) : [];
        }

        var now = time.GetUtcNow();
        var triggers = new HashSet<string>(settings.TriggerApps, StringComparer.OrdinalIgnoreCase);
        var triggerActive = sessions.Any(s => triggers.Contains(s.AppKey) && s.Peak >= settings.Threshold);

        if (triggerActive)
        {
            _lastTriggerActivity = now;
            IsDucked = true;
        }
        else if (IsDucked && now - _lastTriggerActivity >= TimeSpan.FromMilliseconds(settings.ReleaseDelayMs))
        {
            return Restore(sessions);
        }

        if (!IsDucked)
        {
            return [];
        }

        // 消えたセッションの記録は捨てる
        var alive = sessions.Select(s => s.SessionId).ToHashSet();
        foreach (var gone in _originals.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _originals.Remove(gone);
        }

        // 下げ中に新しく出てきたセッションも下げる
        var ratio = settings.DuckLevelPercent / 100f;
        var changes = new List<VolumeChange>();
        foreach (var session in sessions)
        {
            if (triggers.Contains(session.AppKey) || _originals.ContainsKey(session.SessionId))
            {
                continue;
            }

            _originals[session.SessionId] = session.Volume;
            changes.Add(new VolumeChange(session.SessionId, session.Volume * ratio));
        }

        return changes;
    }

    /// <summary>下げたものをすべて元に戻す（ダッキングを切ったときやアプリ終了時）。</summary>
    public IReadOnlyList<VolumeChange> Restore(IReadOnlyList<SessionLevel> sessions)
    {
        var alive = sessions.Select(s => s.SessionId).ToHashSet();
        var changes = _originals
            .Where(pair => alive.Contains(pair.Key))
            .Select(pair => new VolumeChange(pair.Key, pair.Value))
            .ToList();
        _originals.Clear();
        IsDucked = false;
        return changes;
    }
}
