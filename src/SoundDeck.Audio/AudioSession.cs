using SoundDeck.Audio.Interop;
using SoundDeck.Core.Commands;

namespace SoundDeck.Audio;

public enum AudioSessionState
{
    Inactive,
    Active,
    Expired,
}

/// <summary>アプリが 1 つのデバイスに対して開いている音声の流れ。音量・ミュート・メーターを持つ。</summary>
public sealed class AudioSession : IDisposable
{
    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation? _meter;
    private readonly SessionEvents _events;
    private bool _disposed;

    internal AudioSession(IAudioSessionControl2 control, string instanceId, string deviceId, DeviceFlow flow)
    {
        _control = control;
        _volume = (ISimpleAudioVolume)control;
        _meter = control as IAudioMeterInformation;
        InstanceId = instanceId;
        DeviceId = deviceId;
        Flow = flow;

        control.GetSessionIdentifier(out var sessionIdentifier);
        SessionIdentifier = sessionIdentifier ?? "";
        control.GetProcessId(out var processId);
        ProcessId = processId;
        IsSystemSounds = control.IsSystemSoundsSession() == 0;
        control.GetDisplayName(out var displayName);
        DisplayName = ComHelpers.ResolveIndirectString(displayName);
        control.GetIconPath(out var iconPath);
        IconPath = string.IsNullOrEmpty(iconPath) ? null : iconPath;
        control.GetState(out var state);
        State = (AudioSessionState)state;

        _events = new SessionEvents(this);
        ComHelpers.RunInMta(() => _control.RegisterAudioSessionNotification(_events));
    }

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public event EventHandler<AudioSessionState>? StateChanged;

    public event EventHandler? Disconnected;

    /// <summary>セッションごとに一意な ID。</summary>
    public string InstanceId { get; }

    /// <summary>アプリ単位でまとまる ID（同じアプリの別プロセスでも同じになることがある）。</summary>
    public string SessionIdentifier { get; }

    public string DeviceId { get; }

    public DeviceFlow Flow { get; }

    public uint ProcessId { get; }

    public bool IsSystemSounds { get; }

    /// <summary>アプリが自分で付けた表示名。空のことが多い。</summary>
    public string? DisplayName { get; private set; }

    public string? IconPath { get; }

    public AudioSessionState State { get; private set; }

    public bool IsDisconnected { get; private set; }

    public float Volume
    {
        get => _volume.GetMasterVolume(out var level) >= 0 ? level : 0f;
        set
        {
            var context = AudioDevice.SelfContext;
            _volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref context);
        }
    }

    public bool Muted
    {
        get => _volume.GetMute(out var muted) >= 0 && muted;
        set
        {
            var context = AudioDevice.SelfContext;
            _volume.SetMute(value, ref context);
        }
    }

    public float GetPeak() => _meter is not null && _meter.GetPeakValue(out var peak) >= 0 ? peak : 0f;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ComHelpers.RunInMta(() => _control.UnregisterAudioSessionNotification(_events));
    }

    private sealed class SessionEvents(AudioSession owner) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string newDisplayName, ref Guid eventContext)
        {
            owner.DisplayName = ComHelpers.ResolveIndirectString(newDisplayName);
            return 0;
        }

        public int OnIconPathChanged(string newIconPath, ref Guid eventContext) => 0;

        public int OnSimpleVolumeChanged(float newVolume, bool newMute, ref Guid eventContext)
        {
            owner.VolumeChanged?.Invoke(owner, new VolumeChangedEventArgs(newVolume, newMute, eventContext == AudioDevice.SelfContext, eventContext));
            return 0;
        }

        public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, ref Guid eventContext) => 0;

        public int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext) => 0;

        public int OnStateChanged(AudioSessionStateNative newState)
        {
            owner.State = (AudioSessionState)newState;
            owner.StateChanged?.Invoke(owner, owner.State);
            return 0;
        }

        public int OnSessionDisconnected(int disconnectReason)
        {
            owner.IsDisconnected = true;
            owner.Disconnected?.Invoke(owner, EventArgs.Empty);
            return 0;
        }
    }
}
