using System.Runtime.InteropServices;

namespace SoundDeck.Audio.Interop;

// ここにあるのは Windows が公開していない API。サウンド設定や EarTrumpet などが使っている定義に合わせている。
// Windows の更新で使えなくなる可能性があるため、呼び出し側は失敗を前提に扱うこと。

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigComObject;

/// <summary>既定のオーディオデバイスを切り替える（Windows 7 以降の IPolicyConfig）。</summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr value);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}

/// <summary>
/// アプリ別の出力先を設定する WinRT ファクトリ（Windows.Media.Internal.AudioPolicyConfig）。
/// IInspectable 派生のため、vtable の位置を直接指定して関数ポインタで呼ぶ。
/// </summary>
internal static unsafe partial class AudioPolicyConfigNative
{
    // Windows 10 21H2 以降（Windows 11 を含む）の IID
    private static readonly Guid FactoryIid = new("ab3d4648-e242-459f-b02f-541c70306324");

    // IUnknown(3) + IInspectable(3) + 未使用メソッド 19 個のあとに並ぶ
    private const int SetPersistedDefaultAudioEndpointSlot = 25;
    private const int GetPersistedDefaultAudioEndpointSlot = 26;

    public static IntPtr CreateFactory()
    {
        const string className = "Windows.Media.Internal.AudioPolicyConfig";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var classId));
        try
        {
            var iid = FactoryIid;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, ref iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    public static int SetPersistedDefaultAudioEndpoint(IntPtr factory, uint processId, EDataFlow flow, ERole role, string? deviceId)
    {
        var hstring = IntPtr.Zero;
        if (!string.IsNullOrEmpty(deviceId))
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(deviceId, deviceId.Length, out hstring));
        }

        try
        {
            var vtable = *(IntPtr**)factory;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, EDataFlow, ERole, IntPtr, int>)vtable[SetPersistedDefaultAudioEndpointSlot];
            return fn(factory, processId, flow, role, hstring);
        }
        finally
        {
            if (hstring != IntPtr.Zero)
            {
                WindowsDeleteString(hstring);
            }
        }
    }

    public static string? GetPersistedDefaultAudioEndpoint(IntPtr factory, uint processId, EDataFlow flow, ERole role)
    {
        var vtable = *(IntPtr**)factory;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, EDataFlow, ERole, IntPtr*, int>)vtable[GetPersistedDefaultAudioEndpointSlot];
        IntPtr hstring;
        var hr = fn(factory, processId, flow, role, &hstring);
        if (hr < 0 || hstring == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = WindowsGetStringRawBuffer(hstring, out var length);
            return new string((char*)buffer, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}
