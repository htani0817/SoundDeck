using System.Runtime.InteropServices;

namespace SoundDeck.App.Win32;

/// <summary>通知領域（タスクバー右下）のアイコン。クリック・中クリック・右クリック・ホイールを通知する。</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = NativeMethods.WmApp + 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private string _tooltip = "SoundDeck";
    private bool _added;
    private bool _disposed;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _window.MessageReceived += OnMessage;

        // トレイアイコン上のホイール操作は通知されないため、マウスの生入力を受け取って自分で判定する
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = NativeMethods.RidevInputSink, Target = window.Handle },
        };
        NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    /// <summary>左クリック、またはキーボードで選択された。</summary>
    public event EventHandler? Invoked;

    public event EventHandler? MiddleClicked;

    /// <summary>右クリック。引数はメニューを出す位置（物理ピクセル）。</summary>
    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>アイコンの上でホイールを回した。引数は 120 を 1 段とする回転量。</summary>
    public event EventHandler<int>? WheelScrolled;

    /// <summary>エクスプローラーが再起動してタスクバーが作り直された。</summary>
    public event EventHandler? TaskbarRecreated;

    /// <summary>アイコンと説明文を変える。渡した HICON はこのクラスが破棄する。</summary>
    public void Update(IntPtr icon, string tooltip)
    {
        var old = _icon;
        _icon = icon;
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        var data = CreateData();
        if (!_added)
        {
            _added = NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data);
            data.VersionOrTimeout = NativeMethods.NotifyIconVersion4;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
        }
        else
        {
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        }

        if (old != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(old);
        }
    }

    /// <summary>アイコンの画面上の位置（物理ピクセル）。</summary>
    public Rect? GetIconRect()
    {
        var id = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Hwnd = _window.Handle,
            Id = IconId,
        };
        return NativeMethods.ShellNotifyIconGetRect(ref id, out var rect) >= 0 ? rect : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;
        var data = CreateData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
    }

    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Hwnd = _window.Handle,
        Id = IconId,
        Flags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip,
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = _tooltip,
        Info = "",
        InfoTitle = "",
    };

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: lParam の下位ワードがイベント、wParam がマウス位置
            switch ((uint)NativeMethods.LowWord(e.LParam))
            {
                case NativeMethods.NinSelect:
                case NativeMethods.NinKeySelect:
                    Invoked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WmMButtonUp:
                    MiddleClicked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WmContextMenu:
                    ContextMenuRequested?.Invoke(this, new Point { X = NativeMethods.SignedLowWord(e.WParam), Y = NativeMethods.HighWord(e.WParam) });
                    break;
            }

            e.Handled = true;
        }
        else if (e.Message == NativeMethods.WmInput)
        {
            HandleRawInput(e.LParam);
        }
        else if (e.Message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _added = false;
            TaskbarRecreated?.Invoke(this, EventArgs.Empty);
        }
    }

    private unsafe void HandleRawInput(IntPtr rawInputHandle)
    {
        var headerSize = (uint)sizeof(RawInputHeader);
        var size = 0u;
        NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RidInput, null, ref size, headerSize);
        if (size == 0 || size > 256)
        {
            return;
        }

        var buffer = stackalloc byte[(int)size];
        if (NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RidInput, buffer, ref size, headerSize) != size)
        {
            return;
        }

        var header = (RawInputHeader*)buffer;
        if (header->Type != 0)
        {
            return; // マウス以外
        }

        var mouse = (RawMouse*)(buffer + headerSize);
        if ((mouse->ButtonFlags & NativeMethods.RiMouseWheel) == 0)
        {
            return;
        }

        if (NativeMethods.GetCursorPos(out var cursor) && GetIconRect() is { } rect && rect.Contains(cursor))
        {
            WheelScrolled?.Invoke(this, mouse->ButtonData);
        }
    }
}
