using System.Runtime.InteropServices;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Win32;

/// <summary>ウィジェットに描く中身。</summary>
/// <param name="Glyph">Segoe Fluent Icons の文字（スピーカーの形）。</param>
/// <param name="Text">「75%」など。</param>
/// <param name="Muted">ミュート中なら文字を薄くする。</param>
/// <param name="LightTaskbar">タスクバーがライトテーマなら黒っぽい文字にする。</param>
internal sealed record TaskbarWidgetState(string Glyph, string Text, bool Muted, bool LightTaskbar);

/// <summary>
/// タスクバーに重ねて「アイコン + 音量」を出すウィジェット。通知領域のアイコンはメインモニターにしか出せないので、
/// タスクバーのウィンドウ（Shell_TrayWnd / Shell_SecondaryTrayWnd）の子ウィンドウとして自前で描き、
/// 通知領域（サブモニターでは時計）のすぐ左に置く。
/// </summary>
/// <remarks>
/// エクスプローラーのウィンドウの子にすると入力の処理がエクスプローラーとつながるため、WinUI とは別の専用スレッドで動かし、
/// そのスレッドでは待ちの長い処理をしない（UI スレッドへは非同期で知らせるだけ）。
/// 置き場所の計算（UI オートメーションでエクスプローラーに問い合わせる）はさらに別のスレッドで 2 秒ごとに行う。
/// イベントはウィジェットのスレッドから呼ばれる。
/// </remarks>
internal sealed class TaskbarWidgetHost : IDisposable
{
    private const string WidgetClassName = "SoundDeck.TaskbarWidget";
    private const string ControlClassName = "SoundDeck.TaskbarWidgetControl";
    private const uint WmApplyLayout = NativeMethods.WmApp + 10;
    private const uint WmRender = NativeMethods.WmApp + 11;
    private const uint WmStop = NativeMethods.WmApp + 12;
    private const int LayoutIntervalMs = 2000;

    // 置き場所を探すときに避けなくてよい、タスクバー自身の部品
    private static readonly HashSet<string> s_taskbarClasses =
    [
        "Windows.UI.Composition.DesktopWindowContentBridge",
        "Windows.UI.Input.InputSite.WindowClass",
        "Windows.UI.Core.CoreWindow",
        "Start",
        "TrayDummySearchControl",
        "TrayNotifyWnd",
        "ReBarWindow32",
        "MSTaskSwWClass",
        "MSTaskListWClass",
        "WorkerW",
        WidgetClassName,
    ];

    private readonly object _lock = new();
    private readonly WndProc _wndProc;
    private readonly Thread _widgetThread;
    private readonly Thread _layoutThread;
    private readonly AutoResetEvent _layoutWake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Dictionary<IntPtr, Widget> _widgets = []; // キーはウィジェットのウィンドウ
    private volatile bool _stopping;
    private IntPtr _controlHwnd;

    // ロックで守るもの
    private bool _enabled;
    private WidgetMonitorMode _mode;
    private string? _device;
    private TaskbarWidgetState _state = new(Glyphs.Volume, "", false, false);
    private List<Placement>? _pendingLayout;
    private List<Rect> _screenRects = [];
    private int _reportedCount = -1;
    private bool _createFailureLogged;

    public TaskbarWidgetHost()
    {
        _wndProc = WindowProc;
        _widgetThread = new Thread(RunWidgetThread) { IsBackground = true, Name = "SoundDeck.TaskbarWidget" };
        _widgetThread.SetApartmentState(ApartmentState.STA);
        _widgetThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));

        _layoutThread = new Thread(RunLayoutThread) { IsBackground = true, Name = "SoundDeck.TaskbarWidgetLayout" };
        _layoutThread.Start();
    }

    /// <summary>左クリックされた。</summary>
    public event EventHandler? Clicked;

    public event EventHandler? MiddleClicked;

    /// <summary>右クリック。引数はマウスの位置（物理ピクセル）。</summary>
    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>置き直したあと、出ているウィジェットの数が変わった（最初の 1 回は必ず来る）。</summary>
    public event EventHandler<int>? PlacementChanged;

    /// <summary>出すかどうかと、出すモニターを変える。</summary>
    public void Configure(bool enabled, WidgetMonitorMode mode, string? device)
    {
        lock (_lock)
        {
            _enabled = enabled;
            _mode = mode;
            _device = device;
        }

        Refresh();
    }

    /// <summary>描く中身を変える。</summary>
    public void Update(TaskbarWidgetState state)
    {
        lock (_lock)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        NativeMethods.PostMessage(_controlHwnd, WmRender, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>すぐに置き場所を計算し直す（拡大率やタスクバーが変わったとき）。</summary>
    public void Refresh() => _layoutWake.Set();

    /// <summary>画面上の点がウィジェットの上か（ホイールの判定に使う）。どのスレッドから呼んでもよい。</summary>
    public bool HitTest(Point point)
    {
        lock (_lock)
        {
            return _screenRects.Any(r => r.Contains(point));
        }
    }

    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _layoutWake.Set();
        _layoutThread.Join(TimeSpan.FromSeconds(3));
        NativeMethods.PostMessage(_controlHwnd, WmStop, IntPtr.Zero, IntPtr.Zero);
        _widgetThread.Join(TimeSpan.FromSeconds(3));
    }

    // ---- ウィジェットのスレッド ----

    private void RunWidgetThread()
    {
        try
        {
            RegisterClass(WidgetClassName, NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IdcArrow));
            RegisterClass(ControlClassName, IntPtr.Zero);
            _controlHwnd = NativeMethods.CreateWindowEx(0, ControlClassName, "", 0, 0, 0, 0, 0, NativeMethods.HwndMessage, IntPtr.Zero, NativeMethods.GetModuleHandle(IntPtr.Zero), IntPtr.Zero);
        }
        finally
        {
            _ready.Set();
        }

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private void RegisterClass(string name, IntPtr cursor)
    {
        var className = Marshal.StringToHGlobalUni(name);
        try
        {
            var windowClass = new WndClassExNative
            {
                Size = (uint)Marshal.SizeOf<WndClassExNative>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = NativeMethods.GetModuleHandle(IntPtr.Zero),
                Cursor = cursor,
                ClassName = className,
            };
            NativeMethods.RegisterClassEx(ref windowClass);
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (hwnd == _controlHwnd)
            {
                switch (msg)
                {
                    case WmApplyLayout:
                        ApplyLayout();
                        return IntPtr.Zero;
                    case WmRender:
                        foreach (var widget in _widgets.Values)
                        {
                            Render(widget);
                        }

                        return IntPtr.Zero;
                    case WmStop:
                        foreach (var widget in _widgets.Values.ToList())
                        {
                            NativeMethods.DestroyWindow(widget.Hwnd);
                        }

                        _widgets.Clear();
                        NativeMethods.DestroyWindow(_controlHwnd);
                        NativeMethods.PostQuitMessage(0);
                        return IntPtr.Zero;
                }
            }
            else if (_widgets.TryGetValue(hwnd, out var widget))
            {
                if (HandleWidgetMessage(widget, msg) is { } result)
                {
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // ネイティブへ例外を漏らすとプロセスごと落ちるため、ここで止める
            Log.Error("タスクバーのウィジェットの処理に失敗", ex);
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr? HandleWidgetMessage(Widget widget, uint msg)
    {
        switch (msg)
        {
            case NativeMethods.WmMouseActivate:
                // タスクバーを前面（アクティブ）にしない。フライアウトが開いたままクリックを受けられる
                return NativeMethods.MaNoActivate;
            case NativeMethods.WmMouseMove:
                if (!widget.Hover)
                {
                    widget.Hover = true;
                    var track = new TrackMouseEventData { Size = (uint)Marshal.SizeOf<TrackMouseEventData>(), Flags = NativeMethods.TmeLeave, Hwnd = widget.Hwnd };
                    NativeMethods.TrackMouseEvent(ref track);
                    Render(widget);
                }

                return IntPtr.Zero;
            case NativeMethods.WmMouseLeave:
                widget.Hover = false;
                widget.Pressed = false;
                Render(widget);
                return IntPtr.Zero;
            case NativeMethods.WmLButtonDown:
                widget.Pressed = true;
                Render(widget);
                return IntPtr.Zero;
            case NativeMethods.WmLButtonUp:
                if (widget.Pressed)
                {
                    widget.Pressed = false;
                    Render(widget);
                    Clicked?.Invoke(this, EventArgs.Empty);
                }

                return IntPtr.Zero;
            case NativeMethods.WmMButtonUp:
                MiddleClicked?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            case NativeMethods.WmRButtonUp:
                // 既定の処理に回すとタスクバーのメニューが出るので、ここで止める
                NativeMethods.GetCursorPos(out var cursor);
                ContextMenuRequested?.Invoke(this, cursor);
                return IntPtr.Zero;
            default:
                return null;
        }
    }

    /// <summary>計算済みの置き場所に合わせて、ウィジェットを作る・動かす・消す。</summary>
    private void ApplyLayout()
    {
        List<Placement>? placements;
        lock (_lock)
        {
            placements = _pendingLayout;
            _pendingLayout = null;
        }

        if (placements is null || _stopping)
        {
            return;
        }

        // 置かなくなったタスクバー、エクスプローラーの再起動などで消えたものを片付ける
        foreach (var widget in _widgets.Values.ToList())
        {
            var keep = NativeMethods.IsWindow(widget.Hwnd)
                && NativeMethods.IsWindow(widget.Taskbar)
                && placements.Any(p => p.Taskbar == widget.Taskbar);
            if (!keep)
            {
                NativeMethods.DestroyWindow(widget.Hwnd);
                _widgets.Remove(widget.Hwnd);
            }
        }

        var shown = new List<Rect>();
        foreach (var placement in placements)
        {
            var widget = _widgets.Values.FirstOrDefault(w => w.Taskbar == placement.Taskbar);
            if (widget is null)
            {
                var hwnd = NativeMethods.CreateWindowEx(
                    (uint)NativeMethods.WsExLayered | (uint)NativeMethods.WsExToolWindow,
                    WidgetClassName,
                    "SoundDeck",
                    NativeMethods.WsChild | NativeMethods.WsClipSiblings,
                    placement.X,
                    placement.Y,
                    placement.Width,
                    placement.Height,
                    placement.Taskbar,
                    IntPtr.Zero,
                    NativeMethods.GetModuleHandle(IntPtr.Zero),
                    IntPtr.Zero);
                if (hwnd == IntPtr.Zero)
                {
                    if (!_createFailureLogged)
                    {
                        _createFailureLogged = true;
                        Log.Info($"タスクバーのウィジェットを作れませんでした (Win32 エラー {Marshal.GetLastPInvokeError()})");
                    }

                    continue;
                }

                widget = new Widget(hwnd, placement.Taskbar);
                _widgets[hwnd] = widget;
            }

            var resized = widget.Placement is not { } old || old.Width != placement.Width || old.Height != placement.Height || old.Scale != placement.Scale;
            widget.Placement = placement;

            // タスクバーの中身（XAML）より手前に出す
            NativeMethods.SetWindowPos(widget.Hwnd, NativeMethods.HwndTop, placement.X, placement.Y, placement.Width, placement.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            if (resized)
            {
                Render(widget);
            }

            shown.Add(placement.ScreenRect);
        }

        bool changed;
        lock (_lock)
        {
            _screenRects = shown;
            changed = _reportedCount != shown.Count;
            _reportedCount = shown.Count;
        }

        if (changed)
        {
            PlacementChanged?.Invoke(this, shown.Count);
        }
    }

    private void Render(Widget widget)
    {
        if (widget.Placement is not { } placement)
        {
            return;
        }

        TaskbarWidgetState state;
        lock (_lock)
        {
            state = _state;
        }

        TaskbarWidgetRenderer.Render(widget.Hwnd, placement.Width, placement.Height, placement.Scale, state, widget.Hover, widget.Pressed);
    }

    // ---- 置き場所を計算するスレッド ----

    private void RunLayoutThread()
    {
        // UI オートメーションは MTA で使う（エクスプローラーへの問い合わせ中にウィジェットのスレッドを止めないため）
        const uint CoinitMultithreaded = 0x0;
        var initialized = NativeMethods.CoInitializeEx(IntPtr.Zero, CoinitMultithreaded) >= 0;
        try
        {
            using var locator = new TaskbarTrayLocator();
            while (!_stopping)
            {
                try
                {
                    var placements = ComputeLayout(locator);
                    lock (_lock)
                    {
                        _pendingLayout = placements;
                    }

                    NativeMethods.PostMessage(_controlHwnd, WmApplyLayout, IntPtr.Zero, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    Log.Error("タスクバーのウィジェットの配置に失敗", ex);
                }

                _layoutWake.WaitOne(LayoutIntervalMs);
            }
        }
        finally
        {
            if (initialized)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    private List<Placement> ComputeLayout(TaskbarTrayLocator locator)
    {
        bool enabled;
        WidgetMonitorMode mode;
        string? device;
        lock (_lock)
        {
            enabled = _enabled;
            mode = _mode;
            device = _device;
        }

        var placements = new List<Placement>();
        if (!enabled)
        {
            return placements;
        }

        foreach (var taskbar in FindTaskbars())
        {
            var (monitorDevice, isPrimary) = MonitorList.GetMonitorOf(taskbar);
            var wanted = mode switch
            {
                WidgetMonitorMode.Primary => isPrimary,
                WidgetMonitorMode.Specific => string.Equals(monitorDevice, device, StringComparison.OrdinalIgnoreCase),
                _ => true,
            };
            if (wanted && ComputePlacement(taskbar, locator) is { } placement)
            {
                placements.Add(placement);
            }
        }

        return placements;
    }

    private static IEnumerable<IntPtr> FindTaskbars()
    {
        var primary = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            yield return primary;
        }

        var secondary = IntPtr.Zero;
        while ((secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            yield return secondary;
        }
    }

    /// <summary>通知領域（サブモニターでは時計）のすぐ左、ほかのアプリのウィジェットとも重ならない場所。</summary>
    private static Placement? ComputePlacement(IntPtr taskbar, TaskbarTrayLocator locator)
    {
        if (!NativeMethods.IsWindowVisible(taskbar) || !NativeMethods.GetWindowRect(taskbar, out var bar) || bar.Width <= bar.Height)
        {
            return null; // 縦置きのタスクバーには対応しない
        }

        var monitor = NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToNearest);
        var scale = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) >= 0 ? dpi / 96.0 : 1.0;
        var width = TaskbarWidgetRenderer.MeasureWidth(scale);
        var gap = (int)Math.Round(2 * scale);

        // 通知領域の左端。メインのタスクバーは TrayNotifyWnd、サブモニターは UI オートメーションで時計を探す
        int? trayLeft = null;
        var notify = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && NativeMethods.IsWindowVisible(notify) && NativeMethods.GetWindowRect(notify, out var notifyRect) && notifyRect.Width > 0)
        {
            trayLeft = notifyRect.Left;
        }

        trayLeft ??= locator.FindTrayLeft(taskbar);
        var right = Math.Min(trayLeft ?? bar.Right, bar.Right) - gap;

        // ほかのアプリがタスクバーに置いたウィジェットがあれば、その左へずらす
        var others = new List<Rect>();
        NativeMethods.EnumChildWindows(taskbar, (child, _) =>
        {
            if (NativeMethods.GetParent(child) == taskbar
                && NativeMethods.IsWindowVisible(child)
                && !s_taskbarClasses.Contains(NativeMethods.GetClassName(child))
                && NativeMethods.GetWindowRect(child, out var rect)
                && rect.Width > 0)
            {
                others.Add(rect);
            }

            return true;
        }, IntPtr.Zero);

        var moved = true;
        while (moved)
        {
            moved = false;
            foreach (var other in others)
            {
                if (other.Left < right && other.Right > right - width)
                {
                    right = other.Left - gap;
                    moved = true;
                }
            }
        }

        var left = right - width;
        if (left < bar.Left)
        {
            return null;
        }

        var origin = new Point { X = left, Y = bar.Top };
        NativeMethods.ScreenToClient(taskbar, ref origin);
        var screenRect = new Rect { Left = left, Top = bar.Top, Right = right, Bottom = bar.Bottom };
        return new Placement(taskbar, origin.X, origin.Y, width, bar.Height, scale, screenRect);
    }

    /// <summary>タスクバー 1 本に置くウィジェットの位置（X, Y はタスクバーの中の座標）。</summary>
    private sealed record Placement(IntPtr Taskbar, int X, int Y, int Width, int Height, double Scale, Rect ScreenRect);

    private sealed class Widget(IntPtr hwnd, IntPtr taskbar)
    {
        public IntPtr Hwnd { get; } = hwnd;

        public IntPtr Taskbar { get; } = taskbar;

        public Placement? Placement { get; set; }

        public bool Hover { get; set; }

        public bool Pressed { get; set; }
    }
}
