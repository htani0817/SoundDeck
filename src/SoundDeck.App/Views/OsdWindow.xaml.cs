using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SoundDeck.App.Services;
using SoundDeck.App.Win32;
using Windows.Graphics;

namespace SoundDeck.App.Views;

/// <summary>画面下に一瞬出る通知（マイクのミュート、音量、プロファイル適用など）。フォーカスは奪わない。</summary>
public sealed partial class OsdWindow : Window
{
    private const double WidthDip = 300;
    private const double HeightDip = 56;
    private const double BottomOffsetDip = 72;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        _hwnd = WindowHelper.GetHwnd(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        WindowHelper.MakeNoActivateToolWindow(_hwnd);

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(1600);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => AppWindow.Hide();
    }

    internal void Show(OsdMessage message) => Show(message, null);

    /// <summary><paramref name="anchor"/> を省略するとマウスのあるモニターに出す。</summary>
    internal void Show(OsdMessage message, Point? anchor)
    {
        OsdIcon.Glyph = message.Glyph;
        OsdText.Text = message.Text;
        OsdBar.Visibility = message.Value is null ? Visibility.Collapsed : Visibility.Visible;
        OsdBar.Value = message.Value ?? 0;
        OsdValue.Text = message.Value is { } value ? ((int)Math.Round(value)).ToString() : "";

        // マウスのあるモニターの下中央に出す
        NativeMethods.GetCursorPos(out var cursor);
        var (work, _, scale) = WindowHelper.GetMonitorAt(anchor ?? cursor);
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var x = work.Left + ((work.Width - width) / 2);
        var y = work.Bottom - height - (int)Math.Round(BottomOffsetDip * scale);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

        // 前面に出すがアクティブにはしない（ゲーム中などに入力を奪わないため）
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
