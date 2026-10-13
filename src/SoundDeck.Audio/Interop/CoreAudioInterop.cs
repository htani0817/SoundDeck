using System.Runtime.InteropServices;

namespace SoundDeck.Audio.Interop;

// Windows SDK の mmdeviceapi.h / endpointvolume.h / audiopolicy.h / propsys.h の定義を写したもの。
// COM の vtable はメソッドの宣言順で決まるため、使わないメソッドも順番どおりに並べてある。

internal enum EDataFlow
{
    Render,
    Capture,
    All,
}

internal enum ERole
{
    Console,
    Multimedia,
    Communications,
}

[Flags]
internal enum DeviceStates : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF,
}

internal enum AudioSessionStateNative
{
    Inactive,
    Active,
    Expired,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, int propertyId)
{
    public Guid FormatId = formatId;
    public int PropertyId = propertyId;
}

/// <summary>PROPVARIANT（x64 / ARM64 で 24 バイト）。文字列と UINT32 だけ読めればよい。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Value;
    public IntPtr Value2;

    private const ushort VtLpwstr = 31;
    private const ushort VtUi4 = 19;

    public readonly string? AsString() => VarType == VtLpwstr ? Marshal.PtrToStringUni(Value) : null;

    public readonly uint? AsUInt32() => VarType == VtUi4 ? (uint)(Value.ToInt64() & 0xFFFFFFFF) : null;
}

internal static class PropertyKeys
{
    public static readonly PropertyKey DeviceFriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    public static readonly PropertyKey DeviceInterfaceFriendlyName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
    public static readonly PropertyKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    public static readonly PropertyKey DeviceClassIconPath = new(new Guid("259abffc-50a7-47ce-af08-68c9a7d73366"), 12);
    public static readonly PropertyKey AudioEndpointFormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceStates stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out DeviceStates state);
}

[ComImport]
[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMEndpoint
{
    [PreserveSig]
    int GetDataFlow(out EDataFlow dataFlow);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceStates newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    /// <summary>引数は AUDIO_VOLUME_NOTIFICATION_DATA へのポインタ。</summary>
    [PreserveSig]
    int OnNotify(IntPtr notifyData);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint channelCount);

    [PreserveSig]
    int GetChannelsPeakValues(uint channelCount, [Out, MarshalAs(UnmanagedType.LPArray)] float[] peakValues);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig]
    int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, [MarshalAs(UnmanagedType.IUnknown)] out object sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, [MarshalAs(UnmanagedType.IUnknown)] out object audioVolume);

    // IAudioSessionManager2
    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IAudioSessionNotification sessionNotification);

    [PreserveSig]
    int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig]
    int GetState(out AudioSessionStateNative state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    // IAudioSessionControl2
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);

    [PreserveSig]
    int GetProcessId(out uint processId);

    /// <summary>システム音なら S_OK(0)、それ以外は S_FALSE(1) を返す。</summary>
    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string newDisplayName, ref Guid eventContext);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string newIconPath, ref Guid eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, ref Guid eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, ref Guid eventContext);

    [PreserveSig]
    int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionStateNative newState);

    [PreserveSig]
    int OnSessionDisconnected(int disconnectReason);
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object newSession);
}

internal static class ComIids
{
    public static Guid AudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
    public static Guid AudioMeterInformation = typeof(IAudioMeterInformation).GUID;
    public static Guid AudioSessionManager2 = typeof(IAudioSessionManager2).GUID;

    public const uint ClsCtxAll = 0x17;
    public const uint StgmRead = 0;
}

internal static partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int SHLoadIndirectString(string source, char* outBuffer, uint outBufferLength, IntPtr reserved);
}
