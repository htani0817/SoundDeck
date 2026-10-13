using System.Runtime.InteropServices;
using SoundDeck.Audio.Interop;
using SoundDeck.Core.Commands;

namespace SoundDeck.Audio;

/// <summary>デバイスの形状。値は Windows の EndpointFormFactor と同じ。</summary>
public enum DeviceFormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    Spdif = 8,
    DigitalAudioDisplayDevice = 9,
    Unknown = 10,
}

/// <summary>音量変更の通知。<see cref="IsSelf"/> は SoundDeck 自身が変えたとき true。</summary>
public sealed record VolumeChangedEventArgs(float Volume, bool Muted, bool IsSelf);

/// <summary>1 つのオーディオデバイス（スピーカー、ヘッドホン、マイクなど）。</summary>
public sealed class AudioDevice : IDisposable
{
    /// <summary>SoundDeck が音量を変えたときに付ける印。通知が自分の操作の跳ね返りかを見分ける。</summary>
    internal static readonly Guid SelfContext = new("6a1c2f5e-3b7d-4c39-9f0e-5d8b2a4c7e11");

    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume _volume;
    private readonly IAudioMeterInformation? _meter;
    private readonly EndpointVolumeCallback _callback;
    private AudioSessionCollection? _sessions;
    private bool _disposed;

    internal AudioDevice(string id, IMMDevice device)
    {
        Id = id;
        _device = device;
        _callback = new EndpointVolumeCallback(this);

        var endpoint = (IMMEndpoint)device;
        endpoint.GetDataFlow(out var flow);
        Flow = flow == EDataFlow.Capture ? DeviceFlow.Capture : DeviceFlow.Render;

        (_volume, _meter) = ComHelpers.RunInMta(() =>
        {
            var iid = ComIids.AudioEndpointVolume;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ComIids.ClsCtxAll, IntPtr.Zero, out var volumeObject));
            var volume = (IAudioEndpointVolume)volumeObject;
            Marshal.ThrowExceptionForHR(volume.RegisterControlChangeNotify(_callback));

            var meterIid = ComIids.AudioMeterInformation;
            var meter = device.Activate(ref meterIid, ComIids.ClsCtxAll, IntPtr.Zero, out var meterObject) >= 0
                ? (IAudioMeterInformation)meterObject
                : null;
            return (volume, meter);
        });

        RefreshProperties();
    }

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public string Id { get; }

    public DeviceFlow Flow { get; }

    /// <summary>「スピーカー (Realtek Audio)」のような表示名。</summary>
    public string FriendlyName { get; private set; } = "";

    /// <summary>「スピーカー」のような短い名前。</summary>
    public string Description { get; private set; } = "";

    /// <summary>「Realtek Audio」のようなドライバー側の名前。</summary>
    public string InterfaceName { get; private set; } = "";

    public DeviceFormFactor FormFactor { get; private set; } = DeviceFormFactor.Unknown;

    public float Volume
    {
        get => _volume.GetMasterVolumeLevelScalar(out var level) >= 0 ? level : 0f;
        set
        {
            var context = SelfContext;
            _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref context);
        }
    }

    public bool Muted
    {
        get => _volume.GetMute(out var muted) >= 0 && muted;
        set
        {
            var context = SelfContext;
            _volume.SetMute(value, ref context);
        }
    }

    /// <summary>今の音の大きさ（0〜1）。</summary>
    public float GetPeak() => _meter is not null && _meter.GetPeakValue(out var peak) >= 0 ? peak : 0f;

    public void RefreshProperties()
    {
        if (_device.OpenPropertyStore(ComIids.StgmRead, out var store) < 0)
        {
            return;
        }

        FriendlyName = ComHelpers.ReadString(store, PropertyKeys.DeviceFriendlyName) ?? Id;
        Description = ComHelpers.ReadString(store, PropertyKeys.DeviceDescription) ?? FriendlyName;
        InterfaceName = ComHelpers.ReadString(store, PropertyKeys.DeviceInterfaceFriendlyName) ?? "";
        FormFactor = ComHelpers.ReadUInt32(store, PropertyKeys.AudioEndpointFormFactor) is { } ff && ff <= 10
            ? (DeviceFormFactor)ff
            : DeviceFormFactor.Unknown;
        ComHelpers.Release(store);
    }

    /// <summary>このデバイスで鳴っている（録っている）アプリのセッション一覧。初回呼び出し時に作る。</summary>
    public AudioSessionCollection Sessions => _sessions ??= CreateSessions();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessions?.Dispose();
        ComHelpers.RunInMta(() => _volume.UnregisterControlChangeNotify(_callback));
    }

    private AudioSessionCollection CreateSessions()
    {
        var manager = ComHelpers.RunInMta(() =>
        {
            var iid = ComIids.AudioSessionManager2;
            Marshal.ThrowExceptionForHR(_device.Activate(ref iid, ComIids.ClsCtxAll, IntPtr.Zero, out var managerObject));
            return (IAudioSessionManager2)managerObject;
        });
        return new AudioSessionCollection(manager, Id, Flow);
    }

    private sealed class EndpointVolumeCallback(AudioDevice owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; ...
            var context = Marshal.PtrToStructure<Guid>(notifyData);
            var muted = Marshal.ReadInt32(notifyData, 16) != 0;
            var volume = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(notifyData, 20));
            owner.VolumeChanged?.Invoke(owner, new VolumeChangedEventArgs(volume, muted, context == SelfContext));
            return 0;
        }
    }
}
