using System.Runtime.InteropServices;

namespace SoundDeck.App.Win32;

// タスクバーのウィジェット（タスクバーに重ねる小さなウィンドウ）とモニター一覧のための Win32 API。

internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr lParam);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    public uint Size;
    public Rect Monitor;
    public Rect Work;
    public uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string Device;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Width;
    public int Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BlendFunction
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TrackMouseEventData
{
    public uint Size;
    public uint Flags;
    public IntPtr Hwnd;
    public uint HoverTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Msg
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public Point Point;
    public uint Private;
}

internal static partial class NativeMethods
{
    public const uint WmTimer = 0x0113;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLButtonDown = 0x0201;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmMouseActivate = 0x0021;
    public const uint WmMouseLeave = 0x02A3;
    public const uint WmClose = 0x0010;
    public const int MaNoActivate = 3;

    public const uint WsChild = 0x40000000;
    public const uint WsClipSiblings = 0x04000000;
    public const uint WsExLayered = 0x00080000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    public const uint UlwAlpha = 0x2;
    public const uint TmeLeave = 0x2;
    public const uint MonitorInfoPrimary = 0x1;
    public const int IdcArrow = 32512;

    public static readonly IntPtr HwndTop = IntPtr.Zero;
    public static readonly IntPtr HwndMessage = new(-3);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetParent(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static unsafe partial int GetClassName(IntPtr hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(IntPtr hwnd, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref NativeSize psize, IntPtr hdcSrc, ref Point pptSrc, uint key, ref BlendFunction blend, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(ref TrackMouseEventData data);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref Msg msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial IntPtr DispatchMessage(ref Msg msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint elapse, IntPtr timerFunc);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32(IntPtr hdc, string text, int length, out NativeSize size);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    [LibraryImport("oleaut32.dll")]
    public static partial void SysFreeString(IntPtr bstr);

    public static unsafe string GetClassName(IntPtr hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "";
    }
}
