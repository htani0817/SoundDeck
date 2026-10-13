using System.Runtime.InteropServices;

namespace SoundDeck.App.Win32;

// WinUI 3 に無い機能（トレイアイコン、グローバルホットキー、ウィンドウの細かい制御）のための Win32 API。

internal delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;

    public readonly bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NotifyIconData
{
    public uint Size;
    public IntPtr Hwnd;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr Icon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tip;

    public uint State;
    public uint StateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Info;

    public uint VersionOrTimeout;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string InfoTitle;

    public uint InfoFlags;
    public Guid Guid;
    public IntPtr BalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NotifyIconIdentifier
{
    public uint Size;
    public IntPtr Hwnd;
    public uint Id;
    public Guid Guid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppBarData
{
    public uint Size;
    public IntPtr Hwnd;
    public uint CallbackMessage;
    public uint Edge;
    public Rect Rect;
    public IntPtr Param;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public uint Size;
    public Rect Monitor;
    public Rect Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDevice
{
    public ushort UsagePage;
    public ushort Usage;
    public uint Flags;
    public IntPtr Target;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHeader
{
    public uint Type;
    public uint Size;
    public IntPtr Device;
    public IntPtr WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawMouse
{
    public ushort Flags;
    public ushort Padding;
    public ushort ButtonFlags;
    public short ButtonData;
    public uint RawButtons;
    public int LastX;
    public int LastY;
    public uint ExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IconInfo
{
    public int IsIcon;
    public int HotspotX;
    public int HotspotY;
    public IntPtr MaskBitmap;
    public IntPtr ColorBitmap;
}

internal static partial class NativeMethods
{
    public const uint WmNull = 0x0000;
    public const uint WmDestroy = 0x0002;
    public const uint WmSettingChange = 0x001A;
    public const uint WmContextMenu = 0x007B;
    public const uint WmInput = 0x00FF;
    public const uint WmCommand = 0x0111;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmMButtonUp = 0x0208;
    public const uint WmHotkey = 0x0312;
    public const uint WmDpiChanged = 0x02E0;
    public const uint WmApp = 0x8000;

    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    public const uint NimAdd = 0;
    public const uint NimModify = 1;
    public const uint NimDelete = 2;
    public const uint NimSetVersion = 4;
    public const uint NifMessage = 0x1;
    public const uint NifIcon = 0x2;
    public const uint NifTip = 0x4;
    public const uint NifShowTip = 0x80;
    public const uint NotifyIconVersion4 = 4;

    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80;
    public const long WsExNoActivate = 0x08000000;
    public const long WsExTopmost = 0x8;

    public const uint MfString = 0x0;
    public const uint MfGrayed = 0x1;
    public const uint MfChecked = 0x8;
    public const uint MfPopup = 0x10;
    public const uint MfSeparator = 0x800;
    public const uint MfRadioCheck = 0x200;
    public const uint TpmRightButton = 0x2;
    public const uint TpmReturnCmd = 0x100;
    public const uint TpmBottomAlign = 0x20;
    public const uint TpmRightAlign = 0x8;

    public const uint RidevInputSink = 0x100;
    public const uint RidInput = 0x10000003;
    public const ushort RiMouseWheel = 0x0400;

    public const uint MonitorDefaultToNearest = 2;
    public const uint AbmGetTaskbarPos = 5;

    public const int SmCxSmIcon = 49;
    public const int SmCySmIcon = 50;

    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(ref WndClassExNative windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial IntPtr GetModuleHandle(IntPtr moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    public static partial int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);

    [LibraryImport("shell32.dll")]
    public static partial IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hwnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static unsafe partial uint GetRawInputData(IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial long GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial long SetWindowLongPtr(IntPtr hwnd, int index, long value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(IntPtr menu, uint flags, nuint idNewItem, string? newItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpmParams);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static unsafe partial IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header, uint usage, out void* bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr gdiObject);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFont(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(IntPtr hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(IntPtr hdc, int mode);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(IntPtr hdc, string text, int length, ref Rect rect, uint format);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    // ダークモードのメニューを使うための非公開 API（uxtheme.dll の序数 135 / 136）
    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    public static partial int SetPreferredAppMode(int mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    public static partial void FlushMenuThemes();

    public static short HighWord(IntPtr value) => (short)((value.ToInt64() >> 16) & 0xFFFF);

    public static ushort LowWord(IntPtr value) => (ushort)(value.ToInt64() & 0xFFFF);

    public static short SignedLowWord(IntPtr value) => (short)(value.ToInt64() & 0xFFFF);
}

/// <summary>RegisterClassEx に渡す形（文字列をポインタで持つ）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WndClassExNative
{
    public uint Size;
    public uint Style;
    public IntPtr WndProc;
    public int ClsExtra;
    public int WndExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    public IntPtr MenuName;
    public IntPtr ClassName;
    public IntPtr IconSmall;
}
