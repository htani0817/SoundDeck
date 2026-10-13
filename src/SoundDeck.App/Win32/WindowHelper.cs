using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace SoundDeck.App.Win32;

internal enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

internal static class WindowHelper
{
    public static IntPtr GetHwnd(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    /// <summary>Alt+Tab に出さず、表示してもフォーカスを奪わないウィンドウにする（OSD 用）。</summary>
    public static void MakeNoActivateToolWindow(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTopmost);
    }

    public static double GetScale(IntPtr hwnd) => NativeMethods.GetDpiForWindow(hwnd) / 96.0;

    /// <summary>指定した点があるモニターの作業領域（タスクバーを除いた範囲）と拡大率。</summary>
    public static (Rect WorkArea, Rect Monitor, double Scale) GetMonitorAt(Point point)
    {
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) >= 0 ? dpi / 96.0 : 1.0;
        return (info.Work, info.Monitor, scale);
    }

    public static TaskbarEdge GetTaskbarEdge()
    {
        var data = new AppBarData { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<AppBarData>() };
        return NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) != IntPtr.Zero
            ? (TaskbarEdge)data.Edge
            : TaskbarEdge.Bottom;
    }

    /// <summary>タスクバーがライトテーマか（トレイアイコンの色を決める）。</summary>
    public static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    /// <summary>アプリ（ウィンドウ側）がライトテーマか。</summary>
    public static bool IsAppLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value == 1;
    }

    public static int GetSmallIconSize()
    {
        var dpi = NativeMethods.GetDpiForSystem();
        return NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi);
    }
}
