using SoundDeck.Audio.Interop;
using SoundDeck.Core.Commands;

namespace SoundDeck.Audio;

public sealed record SessionRefreshResult(IReadOnlyList<AudioSession> Added, IReadOnlyList<AudioSession> Removed);

/// <summary>
/// 1 つのデバイス上のセッション一覧。
/// 新しいセッションの通知を受けたら <see cref="SessionCreated"/> を出すので、UI スレッドで <see cref="Refresh"/> を呼ぶ。
/// </summary>
public sealed class AudioSessionCollection : IDisposable
{
    private readonly IAudioSessionManager2 _manager;
    private readonly SessionNotification _notification;
    private readonly Dictionary<string, AudioSession> _sessions = [];
    private readonly string _deviceId;
    private readonly DeviceFlow _flow;
    private bool _disposed;

    internal AudioSessionCollection(IAudioSessionManager2 manager, string deviceId, DeviceFlow flow)
    {
        _manager = manager;
        _deviceId = deviceId;
        _flow = flow;
        _notification = new SessionNotification(this);

        ComHelpers.RunInMta(() =>
        {
            // 通知を受けるには、先に一度セッション一覧を取得しておく必要がある
            if (_manager.GetSessionEnumerator(out var enumerator) >= 0)
            {
                ComHelpers.Release(enumerator);
            }

            _manager.RegisterSessionNotification(_notification);
        });
    }

    /// <summary>新しいセッションができた（OS のスレッドから呼ばれる）。</summary>
    public event EventHandler? SessionCreated;

    public IReadOnlyCollection<AudioSession> Sessions => _sessions.Values;

    /// <summary>OS 上のセッション一覧と突き合わせ、増えたもの・消えたものを返す。</summary>
    /// <remarks>取得したセッションを MTA でもイベント登録に使うため、列挙は MTA で行う（呼び出し元はその間待つ）。</remarks>
    public SessionRefreshResult Refresh() => ComHelpers.RunInMta(RefreshCore);

    private SessionRefreshResult RefreshCore()
    {
        var added = new List<AudioSession>();
        var seen = new HashSet<string>();

        if (_manager.GetSessionEnumerator(out var enumerator) >= 0)
        {
            enumerator.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (enumerator.GetSession(i, out var sessionObject) < 0 || sessionObject is not IAudioSessionControl2 control)
                {
                    continue;
                }

                if (control.GetSessionInstanceIdentifier(out var instanceId) < 0 || string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                control.GetState(out var state);
                control.GetProcessId(out var processId);
                var isSystem = control.IsSystemSoundsSession() == 0;
                if (state == AudioSessionStateNative.Expired || (!isSystem && !ProcessInfo.IsAlive(processId)))
                {
                    continue;
                }

                seen.Add(instanceId);
                if (_sessions.ContainsKey(instanceId))
                {
                    continue;
                }

                try
                {
                    var session = new AudioSession(control, instanceId, _deviceId, _flow);
                    _sessions[instanceId] = session;
                    added.Add(session);
                }
                catch (InvalidCastException)
                {
                    // 音量やメーターを持たない特殊なセッションは扱わない
                }
            }

            ComHelpers.Release(enumerator);
        }

        var removed = _sessions.Values
            .Where(s => !seen.Contains(s.InstanceId) || s.IsDisconnected)
            .ToList();
        foreach (var session in removed)
        {
            _sessions.Remove(session.InstanceId);
            session.Dispose();
        }

        return new SessionRefreshResult(added, removed);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ComHelpers.RunInMta(() => _manager.UnregisterSessionNotification(_notification));
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    private sealed class SessionNotification(AudioSessionCollection owner) : IAudioSessionNotification
    {
        public int OnSessionCreated(object newSession)
        {
            // 受け取ったオブジェクトは使わず、UI スレッドでの Refresh で取り直す
            owner.SessionCreated?.Invoke(owner, EventArgs.Empty);
            return 0;
        }
    }
}
