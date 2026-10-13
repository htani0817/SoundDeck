using System.Runtime.InteropServices;

namespace SoundDeck.Audio;

/// <summary>セッションの持ち主プロセスについて調べる。</summary>
public static partial class ProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitTimeout = 0x102;

    /// <summary>実行ファイルのフルパス。管理者権限のプロセスなどで取れなければ null。</summary>
    public static unsafe string? GetExecutablePath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>プロセスがまだ動いているか。調べられないときは動いているとみなす。</summary>
    public static bool IsAlive(uint processId)
    {
        if (processId == 0)
        {
            return true;
        }

        var handle = OpenProcess(Synchronize, false, processId);
        if (handle == IntPtr.Zero)
        {
            // 権限不足なら存在はしている。存在しなければ ERROR_INVALID_PARAMETER(87)
            return Marshal.GetLastPInvokeError() != 87;
        }

        try
        {
            return WaitForSingleObject(handle, 0) == WaitTimeout;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* exeName, ref uint size);
}
