using System.Runtime.InteropServices;
using SoundDeck.Audio.Interop;
using SoundDeck.Core.Commands;

namespace SoundDeck.Audio;

public enum AudioRole
{
    Console,
    Multimedia,
    Communications,
}

public sealed record DefaultDeviceChangedEventArgs(DeviceFlow Flow, AudioRole Role, string? DeviceId);

/// <summary>未接続のものも含めたデバイスの概要（ルールの対象選びに使う）。</summary>
public sealed record AudioDeviceSummary(string Id, DeviceFlow Flow, string Name, bool IsActive);

/// <summary>
/// オーディオデバイスの一覧取得と、追加・削除・既定変更の通知をまとめる。
/// 通知は OS のスレッドから届くので、UI に反映するときは呼び出し側で UI スレッドへ戻すこと。
/// </summary>
public sealed class AudioDeviceManager : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _client;
    private bool _disposed;

    public AudioDeviceManager()
    {
        _client = new NotificationClient(this);
        _enumerator = ComHelpers.RunInMta(() =>
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(_client));
            return enumerator;
        });
    }

    /// <summary>デバイスが有効になった（接続された）。</summary>
    public event EventHandler<string>? DeviceActivated;

    /// <summary>デバイスが無効になった・外された。</summary>
    public event EventHandler<string>? DeviceDeactivated;

    public event EventHandler<DefaultDeviceChangedEventArgs>? DefaultDeviceChanged;

    /// <summary>表示名などのプロパティが変わった。</summary>
    public event EventHandler<string>? DevicePropertyChanged;

    public IReadOnlyList<string> GetActiveDeviceIds(DeviceFlow flow) => ComHelpers.RunInMta(() => GetActiveDeviceIdsCore(flow));

    private List<string> GetActiveDeviceIdsCore(DeviceFlow flow)
    {
        var ids = new List<string>();
        if (_enumerator.EnumAudioEndpoints(ToNative(flow), DeviceStates.Active, out var collection) < 0)
        {
            return ids;
        }

        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) >= 0)
                {
                    // 個別デバイスの RCW は他のラッパーと共有されうるので解放しない（GC に任せる）
                    if (device.GetId(out var id) >= 0)
                    {
                        ids.Add(id);
                    }
                }
            }
        }
        finally
        {
            ComHelpers.Release(collection);
        }

        return ids;
    }

    /// <summary>無効・未接続も含めたデバイスの一覧。削除済みの幽霊デバイスは含めない。</summary>
    public IReadOnlyList<AudioDeviceSummary> GetDeviceSummaries(DeviceFlow flow) => ComHelpers.RunInMta(() => GetDeviceSummariesCore(flow));

    private List<AudioDeviceSummary> GetDeviceSummariesCore(DeviceFlow flow)
    {
        var result = new List<AudioDeviceSummary>();
        var mask = DeviceStates.Active | DeviceStates.Disabled | DeviceStates.Unplugged;
        if (_enumerator.EnumAudioEndpoints(ToNative(flow), mask, out var collection) < 0)
        {
            return result;
        }

        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0 || device.GetId(out var id) < 0)
                {
                    continue;
                }

                device.GetState(out var state);
                var name = id;
                if (device.OpenPropertyStore(ComIids.StgmRead, out var store) >= 0)
                {
                    name = ComHelpers.ReadString(store, PropertyKeys.DeviceFriendlyName) ?? id;
                    ComHelpers.Release(store);
                }

                result.Add(new AudioDeviceSummary(id, flow, name, state == DeviceStates.Active));
            }
        }
        finally
        {
            ComHelpers.Release(collection);
        }

        return result;
    }

    public string? GetDefaultDeviceId(DeviceFlow flow, AudioRole role = AudioRole.Multimedia) => ComHelpers.RunInMta(() => GetDefaultDeviceIdCore(flow, role));

    private string? GetDefaultDeviceIdCore(DeviceFlow flow, AudioRole role)
    {
        if (_enumerator.GetDefaultAudioEndpoint(ToNative(flow), (ERole)role, out var device) < 0)
        {
            return null;
        }

        return device.GetId(out var id) >= 0 ? id : null;
    }

    /// <summary>デバイスを開く。見つからなければ null。返した <see cref="AudioDevice"/> は呼び出し側で Dispose する。</summary>
    public AudioDevice? OpenDevice(string id) => ComHelpers.RunInMta(() =>
    {
        // UI スレッド（STA）で取得したデバイスは MTA へ持ち出せないため、取得からラッパー作成まで MTA で行う
        if (_enumerator.GetDevice(id, out var device) < 0)
        {
            return null;
        }

        try
        {
            return new AudioDevice(id, device);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ComHelpers.RunInMta(() => _enumerator.UnregisterEndpointNotificationCallback(_client));
        ComHelpers.Release(_enumerator);
    }

    internal static EDataFlow ToNative(DeviceFlow flow) => flow == DeviceFlow.Render ? EDataFlow.Render : EDataFlow.Capture;

    private sealed class NotificationClient(AudioDeviceManager owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, DeviceStates newState)
        {
            if (newState == DeviceStates.Active)
            {
                owner.DeviceActivated?.Invoke(owner, deviceId);
            }
            else
            {
                owner.DeviceDeactivated?.Invoke(owner, deviceId);
            }

            return 0;
        }

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId)
        {
            owner.DeviceDeactivated?.Invoke(owner, deviceId);
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow != EDataFlow.All)
            {
                var managed = flow == EDataFlow.Render ? DeviceFlow.Render : DeviceFlow.Capture;
                owner.DefaultDeviceChanged?.Invoke(owner, new DefaultDeviceChangedEventArgs(managed, (AudioRole)role, defaultDeviceId));
            }

            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            owner.DevicePropertyChanged?.Invoke(owner, deviceId);
            return 0;
        }
    }
}
