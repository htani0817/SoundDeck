using System.Runtime.InteropServices;
using SoundDeck.Audio.Interop;
using SoundDeck.Core.Commands;

namespace SoundDeck.Audio;

/// <summary>既定デバイスの切り替え（非公開 API の IPolicyConfig を使う）。</summary>
public static class DefaultDeviceSwitcher
{
    /// <summary>既定デバイスにする。<paramref name="roles"/> を省略すると通常用（Console と Multimedia）。</summary>
    public static void SetDefault(string deviceId, params AudioRole[] roles)
    {
        if (roles.Length == 0)
        {
            roles = [AudioRole.Console, AudioRole.Multimedia];
        }

        var policy = (IPolicyConfig)new PolicyConfigComObject();
        try
        {
            foreach (var role in roles)
            {
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, (ERole)role));
            }
        }
        finally
        {
            ComHelpers.Release(policy);
        }
    }
}

/// <summary>
/// アプリ別の出力先・入力元（Windows の「アプリの音量とデバイスの基本設定」と同じ機能）。
/// 非公開 API のため、使えない環境では <see cref="IsSupported"/> が false になる。UI スレッドから呼ぶこと。
/// </summary>
public sealed class AppRouting : IDisposable
{
    private const string MmDevApiPrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterfaceSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureInterfaceSuffix = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";

    private IntPtr _factory;

    public AppRouting()
    {
        try
        {
            _factory = AudioPolicyConfigNative.CreateFactory();
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or InvalidCastException)
        {
            _factory = IntPtr.Zero;
        }
    }

    public bool IsSupported => _factory != IntPtr.Zero;

    /// <summary>プロセスの出力先（入力元）を指定する。null で「既定に従う」に戻す。</summary>
    public bool SetDevice(uint processId, DeviceFlow flow, string? deviceId)
    {
        if (!IsSupported)
        {
            return false;
        }

        var policyId = deviceId is null ? null : ToPolicyId(deviceId, flow);
        var nativeFlow = AudioDeviceManager.ToNative(flow);
        var ok = true;
        foreach (var role in new[] { ERole.Console, ERole.Multimedia })
        {
            ok &= AudioPolicyConfigNative.SetPersistedDefaultAudioEndpoint(_factory, processId, nativeFlow, role, policyId) >= 0;
        }

        return ok;
    }

    /// <summary>プロセスに指定されている出力先（入力元）。指定がなければ null。</summary>
    public string? GetDevice(uint processId, DeviceFlow flow)
    {
        if (!IsSupported)
        {
            return null;
        }

        var policyId = AudioPolicyConfigNative.GetPersistedDefaultAudioEndpoint(_factory, processId, AudioDeviceManager.ToNative(flow), ERole.Multimedia);
        return FromPolicyId(policyId);
    }

    public void Dispose()
    {
        if (_factory != IntPtr.Zero)
        {
            Marshal.Release(_factory);
            _factory = IntPtr.Zero;
        }
    }

    internal static string ToPolicyId(string deviceId, DeviceFlow flow) =>
        MmDevApiPrefix + deviceId + (flow == DeviceFlow.Render ? RenderInterfaceSuffix : CaptureInterfaceSuffix);

    internal static string? FromPolicyId(string? policyId)
    {
        if (string.IsNullOrEmpty(policyId))
        {
            return null;
        }

        if (!policyId.StartsWith(MmDevApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return policyId;
        }

        var body = policyId[MmDevApiPrefix.Length..];
        var hash = body.LastIndexOf('#');
        return hash > 0 ? body[..hash] : body;
    }
}
