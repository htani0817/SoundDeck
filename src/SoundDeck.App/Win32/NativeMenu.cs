namespace SoundDeck.App.Win32;

/// <summary>トレイアイコンの右クリックメニュー（Windows 標準のポップアップメニュー）。</summary>
internal sealed class NativeMenu : IDisposable
{
    private readonly IntPtr _handle = NativeMethods.CreatePopupMenu();
    private readonly Dictionary<int, Action> _actions;
    private readonly Counter _counter;
    private readonly List<NativeMenu> _children = [];
    private readonly bool _isRoot;

    public NativeMenu()
        : this([], new Counter(), isRoot: true)
    {
    }

    private NativeMenu(Dictionary<int, Action> actions, Counter counter, bool isRoot)
    {
        _actions = actions;
        _counter = counter;
        _isRoot = isRoot;
    }

    public void Add(string text, Action action, bool isChecked = false, bool enabled = true, bool radio = false)
    {
        var id = ++_counter.Value;
        _actions[id] = action;
        var flags = NativeMethods.MfString
            | (isChecked ? NativeMethods.MfChecked : 0)
            | (enabled ? 0 : NativeMethods.MfGrayed)
            | (radio ? NativeMethods.MfRadioCheck : 0);
        NativeMethods.AppendMenu(_handle, flags, (nuint)id, text);
    }

    public NativeMenu AddSubmenu(string text, bool enabled = true)
    {
        var child = new NativeMenu(_actions, _counter, isRoot: false);
        _children.Add(child);
        NativeMethods.AppendMenu(_handle, NativeMethods.MfPopup | (enabled ? 0 : NativeMethods.MfGrayed), (nuint)child._handle, text);
        return child;
    }

    public void AddSeparator() => NativeMethods.AppendMenu(_handle, NativeMethods.MfSeparator, 0, null);

    /// <summary>メニューを出し、選ばれた項目の処理を実行する（選ぶまで戻らない）。</summary>
    public void Show(IntPtr owner, Point position)
    {
        // 前面にしておかないと、メニューの外をクリックしても閉じない
        NativeMethods.SetForegroundWindow(owner);
        var command = NativeMethods.TrackPopupMenuEx(
            _handle,
            NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd | NativeMethods.TpmBottomAlign | NativeMethods.TpmRightAlign,
            position.X,
            position.Y,
            owner,
            IntPtr.Zero);
        NativeMethods.PostMessage(owner, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);

        if (command > 0 && _actions.TryGetValue(command, out var action))
        {
            action();
        }
    }

    public void Dispose()
    {
        // 子メニューは親を破棄すると一緒に消える
        if (_isRoot)
        {
            NativeMethods.DestroyMenu(_handle);
        }
    }

    private sealed class Counter
    {
        public int Value;
    }
}
