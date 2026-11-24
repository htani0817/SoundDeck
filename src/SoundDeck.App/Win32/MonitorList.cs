using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SoundDeck.App.Win32;

/// <summary>つながっているモニター 1 台。<paramref name="Device"/> は \\.\DISPLAY1 のような名前で、設定にはこれを保存する。</summary>
internal sealed record MonitorEntry(string Device, Rect Bounds, bool IsPrimary, string Label);

/// <summary>モニターの一覧と、メニューや設定画面に出す名前（「ディスプレイ 1 (上)」など）。</summary>
internal static partial class MonitorList
{
    public static List<MonitorEntry> GetAll()
    {
        var raw = new List<(string Device, Rect Bounds, bool IsPrimary)>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            if (TryGetInfo(monitor, out var info))
            {
                raw.Add((info.Device, info.Monitor, (info.Flags & NativeMethods.MonitorInfoPrimary) != 0));
            }

            return true;
        }, IntPtr.Zero);

        var primary = raw.FirstOrDefault(m => m.IsPrimary);
        return raw
            .OrderBy(m => DisplayNumber(m.Device))
            .Select(m => new MonitorEntry(m.Device, m.Bounds, m.IsPrimary, MakeLabel(m.Device, m.Bounds, m.IsPrimary, primary.Bounds)))
            .ToList();
    }

    /// <summary>ウィンドウがあるモニターの名前（\\.\DISPLAY1 など）と、メインモニターかどうか。</summary>
    public static (string Device, bool IsPrimary) GetMonitorOf(IntPtr hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        return TryGetInfo(monitor, out var info)
            ? (info.Device, (info.Flags & NativeMethods.MonitorInfoPrimary) != 0)
            : ("", false);
    }

    private static bool TryGetInfo(IntPtr monitor, out MonitorInfoEx info)
    {
        info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), Device = "" };
        return NativeMethods.GetMonitorInfoEx(monitor, ref info);
    }

    private static int DisplayNumber(string device) =>
        DigitsRegex().Match(device) is { Success: true } match && int.TryParse(match.Value, out var number) ? number : int.MaxValue;

    /// <summary>「ディスプレイ 1 (上)」のように、番号とメインモニターから見た向きで呼ぶ。</summary>
    private static string MakeLabel(string device, Rect bounds, bool isPrimary, Rect primary)
    {
        var number = DisplayNumber(device);
        var name = number == int.MaxValue ? device : $"ディスプレイ {number}";
        if (isPrimary)
        {
            return $"{name} (メイン)";
        }

        var dx = (bounds.Left + bounds.Right - primary.Left - primary.Right) / 2;
        var dy = (bounds.Top + bounds.Bottom - primary.Top - primary.Bottom) / 2;
        var direction = Math.Abs(dy) >= Math.Abs(dx)
            ? (dy < 0 ? "上" : "下")
            : (dx < 0 ? "左" : "右");
        return $"{name} ({direction})";
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex DigitsRegex();
}
