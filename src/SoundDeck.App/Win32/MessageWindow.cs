using System.Runtime.InteropServices;

namespace SoundDeck.App.Win32;

internal sealed class WindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    public bool Handled { get; set; }

    public IntPtr Result { get; set; }
}

/// <summary>
/// 画面に出さない Win32 ウィンドウ。トレイアイコンの通知・ホットキー・生入力（ホイール）を受け取る。
/// UI スレッドで作ること（WinUI のメッセージループがこのウィンドウのメッセージも回す）。
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "SoundDeck.MessageWindow";

    // ネイティブ側から呼ばれるので GC に回収されないよう保持する
    private readonly WndProc _wndProc;
    private bool _disposed;

    public MessageWindow()
    {
        _wndProc = WindowProc;
        var instance = NativeMethods.GetModuleHandle(IntPtr.Zero);
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var windowClass = new WndClassExNative
            {
                Size = (uint)Marshal.SizeOf<WndClassExNative>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = instance,
                ClassName = className,
            };

            // 2 回目以降の登録失敗（既に登録済み）は無視してよい
            NativeMethods.RegisterClassEx(ref windowClass);
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }

        Handle = NativeMethods.CreateWindowEx(0, ClassName, "SoundDeck", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"隠しウィンドウを作れませんでした (Win32 エラー {Marshal.GetLastPInvokeError()})");
        }
    }

    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public IntPtr Handle { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.DestroyWindow(Handle);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var args = new WindowMessageEventArgs(msg, wParam, lParam);
        try
        {
            MessageReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            // ネイティブへ例外を漏らすとプロセスごと落ちるため、ここで止める
            Services.Log.Error("ウィンドウメッセージの処理に失敗", ex);
        }

        return args.Handled ? args.Result : NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
