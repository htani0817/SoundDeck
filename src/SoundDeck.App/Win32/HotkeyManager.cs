using SoundDeck.Core.Hotkeys;

namespace SoundDeck.App.Win32;

/// <summary>どのアプリを使っていても効くグローバルホットキーを登録する。</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const uint ModNoRepeat = 0x4000;

    private readonly MessageWindow _window;
    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private readonly Dictionary<Guid, bool> _results = [];
    private List<HotkeyBinding> _bindings = [];
    private bool _suspended;
    private int _nextId = 1;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    public event EventHandler<HotkeyBinding>? Pressed;

    /// <summary>登録できたか（他のアプリが同じキーを使っていると false）。キーはバインドの Id。</summary>
    public IReadOnlyDictionary<Guid, bool> Results => _results;

    public void Apply(IEnumerable<HotkeyBinding> bindings)
    {
        _bindings = [.. bindings];
        if (!_suspended)
        {
            RegisterAll();
        }
    }

    /// <summary>ホットキー入力欄でキーを拾う間だけ、登録を外す。</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        _suspended = false;
        RegisterAll();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.MessageReceived -= OnMessage;
    }

    private void RegisterAll()
    {
        UnregisterAll();
        _results.Clear();
        foreach (var binding in _bindings)
        {
            if (!binding.Enabled || !binding.Gesture.IsValid)
            {
                continue;
            }

            var id = _nextId++;
            var ok = NativeMethods.RegisterHotKey(_window.Handle, id, (uint)binding.Modifiers | ModNoRepeat, (uint)binding.VirtualKey);
            _results[binding.Id] = ok;
            if (ok)
            {
                _registered[id] = binding;
            }
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == NativeMethods.WmHotkey && _registered.TryGetValue((int)e.WParam, out var binding))
        {
            Pressed?.Invoke(this, binding);
            e.Handled = true;
        }
    }
}
