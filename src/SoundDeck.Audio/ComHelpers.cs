using System.Runtime.InteropServices;
using SoundDeck.Audio.Interop;

namespace SoundDeck.Audio;

internal static class ComHelpers
{
    /// <summary>
    /// MTA のスレッドで処理を実行する。
    /// セッション通知の登録は MTA でないと通知が届かない（IAudioSessionManager2 の仕様）ため、
    /// COM オブジェクトの生成と通知の登録はここを通す。生成したオブジェクトは UI スレッドからも呼べる。
    /// </summary>
    public static T RunInMta<T>(Func<T> action) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA
            ? action()
            : Task.Run(action).GetAwaiter().GetResult();

    public static void RunInMta(Action action) => RunInMta(() =>
    {
        action();
        return 0;
    });

    public static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    public static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) < 0)
        {
            return null;
        }

        try
        {
            return value.AsString();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }

    public static uint? ReadUInt32(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) < 0)
        {
            return null;
        }

        try
        {
            return value.AsUInt32();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }

    /// <summary>"@%SystemRoot%\System32\AudioSrv.Dll,-202" のようなリソース参照を文字列にする。</summary>
    public static unsafe string? ResolveIndirectString(string? source)
    {
        if (string.IsNullOrEmpty(source) || source[0] != '@')
        {
            return source;
        }

        var buffer = stackalloc char[512];
        return NativeMethods.SHLoadIndirectString(source, buffer, 512, IntPtr.Zero) >= 0
            ? new string(buffer)
            : null;
    }
}
