using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;
using SoundDeck.App.Views;
using SoundDeck.App.Win32;
using SoundDeck.Core.Commands;
using SoundDeck.Core.Hotkeys;

namespace SoundDeck.App;

/// <summary>
/// アプリ全体の組み立て。メインウィンドウは持たず、トレイアイコンに常駐する。
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\SoundDeck.SingleInstance";
    private const string ActivateEventName = @"Local\SoundDeck.Activate";

    private Mutex? _singleInstance;
    private EventWaitHandle? _activateEvent;
    private DispatcherQueue? _dispatcher;
    private MessageWindow? _messageWindow;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private FlyoutWindow? _flyout;
    private OsdWindow? _osd;
    private SettingsWindow? _settingsWindow;
    private DispatcherQueueTimer? _trayUpdateTimer;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("想定外のエラー", e.Exception);
            e.Handled = true;
        };
    }

    public static new App Current => (App)Application.Current;

    internal SettingsService Settings { get; private set; } = null!;

    internal AudioService Audio { get; private set; } = null!;

    internal HotkeyManager Hotkeys => _hotkeys!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 二重起動したときは、先に動いている方のフライアウトを開いて終わる
        _singleInstance = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }

            Exit();
            return;
        }

        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => _dispatcher.TryEnqueue(() => _flyout?.Show()), null, -1, executeOnlyOnce: false);

        // 右クリックメニューをダークテーマに追従させる（1 = AllowDark）
        try
        {
            NativeMethods.SetPreferredAppMode(1);
            NativeMethods.FlushMenuThemes();
        }
        catch (EntryPointNotFoundException)
        {
        }

        Settings = new SettingsService(_dispatcher);
        Audio = new AudioService(_dispatcher, Settings);
        _flyout = new FlyoutWindow(Audio, Settings);
        _osd = new OsdWindow();

        _messageWindow = new MessageWindow();
        _messageWindow.MessageReceived += OnMessage;
        _tray = new TrayIcon(_messageWindow);
        _tray.Invoked += (_, _) => _flyout.Toggle();
        _tray.MiddleClicked += (_, _) => Audio.ToggleDefaultMute(showOsd: false);
        _tray.ContextMenuRequested += (_, point) => ShowTrayMenu(point);
        _tray.WheelScrolled += (_, delta) => Audio.AdjustDefaultVolume(Math.Sign(delta) * Settings.Current.General.ScrollStepPercent, showOsd: false);
        _tray.TaskbarRecreated += (_, _) => UpdateTrayIcon();

        _hotkeys = new HotkeyManager(_messageWindow);
        _hotkeys.Pressed += (_, binding) => OnHotkey(binding);
        _hotkeys.Apply(Settings.Current.Hotkeys);

        _trayUpdateTimer = _dispatcher.CreateTimer();
        _trayUpdateTimer.Interval = TimeSpan.FromMilliseconds(60);
        _trayUpdateTimer.IsRepeating = false;
        _trayUpdateTimer.Tick += (_, _) => UpdateTrayIcon();

        Audio.DefaultOutputStateChanged += (_, _) => ScheduleTrayUpdate();
        Audio.OsdRequested += (_, message) =>
        {
            if (Settings.Current.General.ShowOsd)
            {
                _osd.Show(message);
            }
        };
        Settings.Changed += (_, _) =>
        {
            Audio.ApplySettings();
            _hotkeys.Apply(Settings.Current.Hotkeys);
        };

        SyncStartupRegistration();
        UpdateTrayIcon();
        Log.Info($"起動しました (アプリ別出力先: {(Audio.IsAppRoutingSupported ? "対応" : "非対応")})");

        try
        {
            if (ShowPreviewFromArgs())
            {
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("確認用表示に失敗", ex);
            return;
        }

        if (!Settings.Current.WelcomeShown)
        {
            // 初めて使う人はアイコンの場所が分からない（「^」の中に隠れることも多い）ので、一度だけ知らせる
            _tray.ShowNotification(
                "SoundDeck を起動しました",
                "通知領域のスピーカーのアイコンをクリックすると音量ミキサーが開きます。見当たらないときはタスクバー右端の「^」の中を確認してください。");
            Settings.Current.WelcomeShown = true;
            Settings.ScheduleSave();
        }

        if (Environment.GetCommandLineArgs().Contains("--settings"))
        {
            OpenSettings();
        }
        else if (Environment.GetCommandLineArgs().Contains("--show"))
        {
            _flyout.Show();
        }
    }

    /// <summary>
    /// 見た目確認用の起動オプション。フォーカスを奪わずに指定モニターへ表示する（x,y はそのモニター上の点）。
    /// --preview-flyout=x,y[,input] / --preview-settings=x,y,ページ名 / --preview-osd=x,y
    /// </summary>
    private bool ShowPreviewFromArgs()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            var eq = arg.IndexOf('=');
            if (!arg.StartsWith("--preview-", StringComparison.Ordinal) || eq < 0)
            {
                continue;
            }

            var parts = arg[(eq + 1)..].Split(',');
            var anchor = new Point { X = int.Parse(parts[0]), Y = int.Parse(parts[1]) };
            switch (arg[..eq])
            {
                case "--preview-flyout":
                    _flyout!.ShowPreview(anchor, parts.Length > 2 && parts[2] == "input");
                    return true;
                case "--preview-settings":
                    _settingsWindow = new SettingsWindow();
                    _settingsWindow.Navigate(parts.Length > 2 ? parts[2] : null);
                    _settingsWindow.ShowPreview(anchor);
                    return true;
                case "--preview-osd":
                    _osd!.Show(new OsdMessage(Glyphs.MicrophoneOff, "マイク: ミュート"), anchor);
                    return true;
            }
        }

        return false;
    }

    /// <summary>設定ウィンドウを開く。<paramref name="page"/> で最初に出すページを選べる。</summary>
    internal void OpenSettings(string? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Navigate(page);
        _settingsWindow.Activate();
    }

    internal void Quit()
    {
        Settings.SaveNow();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Audio.Dispose();
        _messageWindow?.Dispose();
        _settingsWindow?.Close();
        _singleInstance?.ReleaseMutex();
        Exit();
    }

    private void SyncStartupRegistration()
    {
        // 実行ファイルの場所が変わっていても登録し直す
        try
        {
            StartupRegistration.Set(Settings.Current.General.LaunchAtStartup);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log.Error("自動起動の登録に失敗", ex);
        }
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == NativeMethods.WmSettingChange)
        {
            // テーマや拡大率が変わったらアイコンを描き直す
            ScheduleTrayUpdate();
        }
    }

    private void ScheduleTrayUpdate()
    {
        _trayUpdateTimer?.Stop();
        _trayUpdateTimer?.Start();
    }

    private void UpdateTrayIcon()
    {
        if (_tray is null)
        {
            return;
        }

        var device = Audio.DefaultOutput;
        var glyph = device is null ? Glyphs.Mute : Glyphs.ForVolume(device.Volume, device.IsMuted);
        var color = WindowHelper.IsTaskbarLight() ? 0x1A1A1Au : 0xFFFFFFu;
        var icon = GlyphIconRenderer.Render(glyph, color, WindowHelper.GetSmallIconSize());
        var tooltip = device is null
            ? "SoundDeck: 出力デバイスがありません"
            : $"{device.FullName}: {(device.IsMuted ? "ミュート" : $"{device.Volume:0}%")}";
        _tray.Update(icon, tooltip);
    }

    private void ShowTrayMenu(Point point)
    {
        using var menu = new NativeMenu();

        AddDeviceSubmenu(menu, "出力デバイス", DeviceFlow.Render, Audio.DefaultOutputId);
        AddDeviceSubmenu(menu, "入力デバイス", DeviceFlow.Capture, Audio.DefaultInputId);

        var profiles = menu.AddSubmenu("プロファイル", Settings.Current.Profiles.Count > 0);
        foreach (var profile in Settings.Current.Profiles)
        {
            profiles.Add(profile.Name, () => Audio.ApplyProfile(profile));
        }

        menu.AddSeparator();
        menu.Add("出力をミュート", () => Audio.ToggleDefaultMute(showOsd: false), isChecked: Audio.DefaultOutput?.IsMuted == true, enabled: Audio.DefaultOutput is not null);
        menu.Add("マイクをミュート", () => Audio.ToggleMicMute(), isChecked: Audio.DefaultInput?.IsMuted == true, enabled: Audio.DefaultInput is not null);
        menu.AddSeparator();
        menu.Add("Windows のサウンド設定", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound")));
        menu.Add("Windows の音量ミキサー", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:apps-volume")));
        menu.Add("SoundDeck の設定", () => OpenSettings());
        menu.AddSeparator();
        menu.Add("終了", Quit);

        menu.Show(_messageWindow!.Handle, point);
    }

    private void AddDeviceSubmenu(NativeMenu menu, string title, DeviceFlow flow, string? defaultId)
    {
        var devices = Audio.GetAllDevices(flow).Where(d => d.IsDefault || !Audio.IsHidden(d.Id)).ToList();
        var submenu = menu.AddSubmenu(title, devices.Count > 0);
        foreach (var device in devices)
        {
            var id = device.Id;
            var isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
            submenu.Add(device.FullName, () => Audio.SetDefaultDevice(id), isChecked: isDefault, radio: true);
        }
    }

    private void OnHotkey(HotkeyBinding binding)
    {
        var step = Settings.Current.General.ScrollStepPercent;
        switch (binding.Action)
        {
            case HotkeyAction.ToggleFlyout:
                _flyout?.Toggle();
                break;
            case HotkeyAction.ToggleMicMute:
                Audio.ToggleMicMute();
                break;
            case HotkeyAction.DefaultVolumeUp:
                Audio.AdjustDefaultVolume(step, showOsd: true);
                break;
            case HotkeyAction.DefaultVolumeDown:
                Audio.AdjustDefaultVolume(-step, showOsd: true);
                break;
            case HotkeyAction.ToggleDefaultMute:
                Audio.ToggleDefaultMute(showOsd: true);
                break;
            case HotkeyAction.CycleDefaultOutput:
                Audio.CycleDefaultOutput();
                break;
            case HotkeyAction.ApplyProfile:
                if (Settings.Current.Profiles.FirstOrDefault(p => p.Id == binding.ProfileId) is { } profile)
                {
                    Audio.ApplyProfile(profile);
                }

                break;
            case HotkeyAction.OpenSettings:
                OpenSettings();
                break;
        }
    }
}
