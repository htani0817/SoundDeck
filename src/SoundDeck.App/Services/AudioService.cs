using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using SoundDeck.App.ViewModels;
using SoundDeck.Audio;
using SoundDeck.Core.Commands;
using SoundDeck.Core.Ducking;
using SoundDeck.Core.Profiles;
using SoundDeck.Core.Rules;
using SoundDeck.Core.VolumeMemory;

namespace SoundDeck.App.Services;

/// <summary>OSD に出す内容。</summary>
internal sealed record OsdMessage(string Glyph, string Text, double? Value = null);

/// <summary>
/// 音声まわりの中心。デバイスとセッションを追いかけて ViewModel に反映し、
/// 音量記憶・自動切替ルール・ダッキング・プロファイル適用をここで実行する。
/// UI スレッド専用（OS からの通知はすべて UI スレッドへ戻してから処理する）。
/// </summary>
internal sealed class AudioService : IDisposable
{
    private const float VolumeTolerance = 0.005f;

    private readonly DispatcherQueue _dispatcher;
    private readonly SettingsService _settings;
    private readonly AudioDeviceManager _manager;
    private readonly AppRouting _routing;
    private readonly VolumeMemoryService _memory;
    private readonly RuleEngine _rules = new(TimeProvider.System);
    private readonly DuckingController _ducking = new(TimeProvider.System);
    private readonly Dictionary<string, DeviceViewModel> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioSession, SessionInfo> _sessionInfo = [];
    private readonly Dictionary<AudioSession, AppViewModel> _sessionOwners = [];
    private readonly HashSet<string> _pendingPropertyRefresh = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _duckingTimer;
    private readonly DispatcherQueueTimer _pruneTimer;
    private readonly DispatcherQueueTimer _refreshAllTimer;
    private bool _initializing = true;
    private bool _disposed;

    public AudioService(DispatcherQueue dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _memory = new VolumeMemoryService(settings.Current.VolumeMemory, TimeProvider.System);
        _manager = new AudioDeviceManager();
        _routing = new AppRouting();

        _manager.DeviceActivated += (_, id) => Post(() => OnDeviceActivated(id));
        _manager.DeviceDeactivated += (_, id) => Post(() => OnDeviceDeactivated(id));
        _manager.DefaultDeviceChanged += (_, e) => Post(() => OnDefaultDeviceChanged(e));
        _manager.DevicePropertyChanged += (_, id) => Post(() => OnDevicePropertyChanged(id));

        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var id in _manager.GetActiveDeviceIds(flow))
            {
                AddDevice(id);
            }
        }

        DefaultOutputId = _manager.GetDefaultDeviceId(DeviceFlow.Render);
        DefaultInputId = _manager.GetDefaultDeviceId(DeviceFlow.Capture);
        CommunicationsOutputId = _manager.GetDefaultDeviceId(DeviceFlow.Render, AudioRole.Communications);
        CommunicationsInputId = _manager.GetDefaultDeviceId(DeviceFlow.Capture, AudioRole.Communications);
        UpdateDefaultFlags();
        RebuildLists();
        _initializing = false;

        _duckingTimer = CreateTimer(TimeSpan.FromMilliseconds(100), OnDuckingTick);
        _pruneTimer = CreateTimer(TimeSpan.FromSeconds(3), () => RefreshAllSessions());
        _pruneTimer.Start();
        _refreshAllTimer = CreateTimer(TimeSpan.FromMilliseconds(400), () => RefreshAllSessions());
        _refreshAllTimer.IsRepeating = false;
        UpdateDuckingTimer();
    }

    /// <summary>既定の出力デバイスの音量・ミュート・デバイス自体が変わった（トレイアイコン更新用）。</summary>
    public event EventHandler? DefaultOutputStateChanged;

    /// <summary>既定の入力デバイスのミュートなどが変わった。</summary>
    public event EventHandler? DefaultInputStateChanged;

    /// <summary>デバイスの増減や並びが変わった（設定画面の一覧更新用）。</summary>
    public event EventHandler? DevicesChanged;

    public event EventHandler<OsdMessage>? OsdRequested;

    /// <summary>フライアウトに出す出力デバイス（非表示指定を除き、既定を先頭に並べる）。</summary>
    public ObservableCollection<DeviceViewModel> OutputDevices { get; } = [];

    public ObservableCollection<DeviceViewModel> InputDevices { get; } = [];

    public string? DefaultOutputId { get; private set; }

    public string? DefaultInputId { get; private set; }

    public string? CommunicationsOutputId { get; private set; }

    public string? CommunicationsInputId { get; private set; }

    public DeviceViewModel? DefaultOutput => Find(DefaultOutputId);

    public DeviceViewModel? DefaultInput => Find(DefaultInputId);

    public bool IsAppRoutingSupported => _routing.IsSupported;

    public VolumeMemoryService Memory => _memory;

    /// <summary>接続中のすべてのデバイス（非表示指定も含む）。名前順。</summary>
    public IReadOnlyList<DeviceViewModel> GetAllDevices(DeviceFlow flow) =>
        [.. _devices.Values.Where(d => d.Flow == flow).OrderBy(d => d.FullName, StringComparer.CurrentCulture)];

    /// <summary>未接続も含めたデバイスの一覧（つながっているものを先に、名前順）。</summary>
    public IReadOnlyList<AudioDeviceSummary> GetDeviceSummaries(DeviceFlow flow) =>
    [
        .. _manager.GetDeviceSummaries(flow)
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture),
    ];

    public DeviceViewModel? Find(string? id) => id is not null && _devices.TryGetValue(id, out var vm) ? vm : null;

    /// <summary>今動いているアプリ（出力側）の一覧。ダッキングやルールのアプリ選択に使う。</summary>
    public IReadOnlyList<AppViewModel> GetRunningApps() =>
    [
        .. _devices.Values
            .Where(d => d.Flow == DeviceFlow.Render)
            .SelectMany(d => d.Apps)
            .Where(a => !a.IsSystemSounds)
            .GroupBy(a => a.AppKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCulture),
    ];

    // ---- 操作 ----

    public void SetDeviceVolume(DeviceViewModel device, float volume, bool fromUi)
    {
        Try("音量の変更", () =>
        {
            device.Device.Volume = volume;
            if (fromUi && device.IsMuted && volume > 0)
            {
                // スライダーを動かしたらミュートを解除する（Windows のクイック設定と同じ）
                device.IsMuted = false;
            }
            else if (!fromUi)
            {
                device.SyncFromSystem(volume, device.Device.Muted);
            }
        });
        RaiseDefaultStateChanged(device);
    }

    public void SetDeviceMute(DeviceViewModel device, bool muted, bool fromUi)
    {
        Try("ミュートの切り替え", () =>
        {
            device.Device.Muted = muted;
            if (!fromUi)
            {
                device.SyncFromSystem(device.Device.Volume, muted);
            }
        });
        RaiseDefaultStateChanged(device);
    }

    public void SetAppVolume(AppViewModel app, float volume, bool fromUi)
    {
        Try("アプリ音量の変更", () =>
        {
            foreach (var session in app.Sessions)
            {
                session.Volume = volume;
                _ducking.ForgetSession(session.InstanceId);
            }

            if (fromUi && app.IsMuted && volume > 0)
            {
                app.IsMuted = false;
            }
            else if (!fromUi)
            {
                app.SyncFromSystem(volume, app.IsMuted);
            }
        });
        RecordMemory(app);
    }

    public void SetAppMute(AppViewModel app, bool muted, bool fromUi)
    {
        Try("アプリのミュート", () =>
        {
            foreach (var session in app.Sessions)
            {
                session.Muted = muted;
            }

            if (!fromUi)
            {
                app.SyncFromSystem((float)(app.Volume / 100), muted);
            }
        });
        RecordMemory(app);
    }

    /// <summary>アプリの出力先（入力元）を変える。null で既定に戻す。</summary>
    public void SetAppDevice(AppViewModel app, string? deviceId)
    {
        if (!_routing.IsSupported)
        {
            return;
        }

        Try("アプリの出力先の変更", () =>
        {
            foreach (var pid in app.ProcessIds)
            {
                _routing.SetDevice(pid, app.Device.Flow, deviceId);
            }
        });

        if (app.Device.Flow == DeviceFlow.Render && _memory.RecordOutput(app.AppKey, app.DisplayName, deviceId))
        {
            _settings.ScheduleSave();
        }

        // セッションは別のデバイスへ移るので、少し待ってから取り直す
        _refreshAllTimer.Stop();
        _refreshAllTimer.Start();
    }

    /// <summary>アプリに指定されている出力先（既定に従っているなら null）。</summary>
    public string? GetAppDevice(AppViewModel app)
    {
        var pid = app.ProcessIds.FirstOrDefault();
        return pid == 0 ? null : _routing.GetDevice(pid, app.Device.Flow);
    }

    public void SetDefaultDevice(string deviceId)
    {
        if (Find(deviceId) is not { } device)
        {
            return;
        }

        var roles = _settings.Current.General.SwitchCommunicationsDevice
            ? new[] { AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications }
            : [AudioRole.Console, AudioRole.Multimedia];
        Try($"「{device.Name}」への切り替え", () => DefaultDeviceSwitcher.SetDefault(deviceId, roles));
    }

    public void SetCommunicationsDevice(string deviceId) =>
        Try("通信デバイスの切り替え", () => DefaultDeviceSwitcher.SetDefault(deviceId, AudioRole.Communications));

    public bool? ToggleMicMute()
    {
        if (DefaultInput is not { } mic)
        {
            OsdRequested?.Invoke(this, new OsdMessage(Glyphs.MicrophoneOff, "マイクが見つかりません"));
            return null;
        }

        var muted = !mic.IsMuted;
        SetDeviceMute(mic, muted, fromUi: false);
        OsdRequested?.Invoke(this, new OsdMessage(Glyphs.ForMicrophone(muted), muted ? "マイク: ミュート" : "マイク: オン"));
        return muted;
    }

    public void AdjustDefaultVolume(int deltaPercent, bool showOsd)
    {
        if (DefaultOutput is not { } device)
        {
            return;
        }

        var volume = Math.Clamp(device.Volume + deltaPercent, 0, 100);
        SetDeviceVolume(device, (float)(volume / 100), fromUi: false);
        if (device.IsMuted && deltaPercent > 0)
        {
            SetDeviceMute(device, false, fromUi: false);
        }

        if (showOsd)
        {
            OsdRequested?.Invoke(this, new OsdMessage(device.VolumeGlyph, device.Name, device.Volume));
        }
    }

    public void ToggleDefaultMute(bool showOsd)
    {
        if (DefaultOutput is not { } device)
        {
            return;
        }

        SetDeviceMute(device, !device.IsMuted, fromUi: false);
        if (showOsd)
        {
            OsdRequested?.Invoke(this, new OsdMessage(device.VolumeGlyph, device.IsMuted ? $"{device.Name}: ミュート" : device.Name, device.IsMuted ? null : device.Volume));
        }
    }

    /// <summary>既定の出力デバイスを、非表示にしていないデバイスの中で順番に切り替える。</summary>
    public void CycleDefaultOutput()
    {
        var candidates = GetAllDevices(DeviceFlow.Render).Where(d => !IsHidden(d.Id)).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var index = candidates.FindIndex(d => string.Equals(d.Id, DefaultOutputId, StringComparison.OrdinalIgnoreCase));
        var next = candidates[(index + 1) % candidates.Count];
        SetDefaultDevice(next.Id);
        OsdRequested?.Invoke(this, new OsdMessage(next.DeviceGlyph, next.Name));
    }

    // ---- プロファイル ----

    public AudioSnapshot GetSnapshot()
    {
        var devices = _devices.Values
            .Select(d => new DeviceAudioState(d.Id, d.Flow, (float)(d.Volume / 100), d.IsMuted))
            .ToList();
        var apps = _devices.Values
            .Where(d => d.Flow == DeviceFlow.Render)
            .SelectMany(d => d.Apps)
            .GroupBy(a => a.AppKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(a => new AppAudioState(a.AppKey, a.DisplayName, (float)(a.Volume / 100), a.IsMuted, a.IsSystemSounds ? null : GetAppDevice(a)))
            .ToList();
        return new AudioSnapshot(DefaultOutputId, DefaultInputId, devices, apps);
    }

    public void ApplyProfile(Profile profile)
    {
        foreach (var command in ProfilePlanner.Plan(profile, GetSnapshot()))
        {
            switch (command)
            {
                case SetDefaultDeviceCommand c:
                    SetDefaultDevice(c.DeviceId);
                    break;
                case SetDeviceVolumeCommand c when Find(c.DeviceId) is { } device:
                    if (c.Volume is { } v)
                    {
                        SetDeviceVolume(device, v, fromUi: false);
                    }

                    if (c.Muted is { } m)
                    {
                        SetDeviceMute(device, m, fromUi: false);
                    }

                    break;
                case SetAppStateCommand c:
                    ApplyAppState(c);
                    break;
            }
        }

        _settings.ScheduleSave();
        OsdRequested?.Invoke(this, new OsdMessage(Glyphs.Sliders, $"プロファイル: {profile.Name}"));
    }

    private void ApplyAppState(SetAppStateCommand command)
    {
        var apps = _devices.Values
            .Where(d => d.Flow == DeviceFlow.Render)
            .SelectMany(d => d.Apps)
            .Where(a => string.Equals(a.AppKey, command.AppKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var app in apps)
        {
            if (command.Volume is { } v)
            {
                SetAppVolume(app, v, fromUi: false);
            }

            if (command.Muted is { } m)
            {
                SetAppMute(app, m, fromUi: false);
            }
        }

        if (command.SetOutput && apps.Count > 0)
        {
            SetAppDevice(apps[0], command.OutputDeviceId);
        }

        // 起動していないアプリは、次に音を出したときに戻す値として覚える
        _memory.Apply(command);
    }

    // ---- 設定の反映 ----

    /// <summary>設定画面での変更（表示名・非表示・ダッキングなど）を反映する。</summary>
    public void ApplySettings()
    {
        foreach (var device in _devices.Values)
        {
            ApplyCustomization(device);
        }

        RebuildLists(raiseEvent: false);
        UpdateDuckingTimer();
    }

    public bool IsHidden(string deviceId) =>
        _settings.Current.Devices.TryGetValue(deviceId, out var custom) && custom.Hidden;

    /// <summary>フライアウト表示中のメーター更新。</summary>
    public void UpdatePeaks()
    {
        foreach (var device in OutputDevices.Concat(InputDevices))
        {
            device.UpdatePeak(Safe(device.Device.GetPeak));
            foreach (var app in device.Apps)
            {
                app.UpdatePeak();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _duckingTimer.Stop();
        _pruneTimer.Stop();
        _refreshAllTimer.Stop();

        // 下げたままの音量を戻してから終わる
        ApplyVolumeChanges(_ducking.Restore(CollectLevels(out var lookup)), lookup);

        foreach (var device in _devices.Values)
        {
            device.Device.Dispose();
        }

        _devices.Clear();
        _manager.Dispose();
        _routing.Dispose();
    }

    // ---- デバイスの追従 ----

    private void AddDevice(string id)
    {
        if (_devices.ContainsKey(id) || _manager.OpenDevice(id) is not { } device)
        {
            return;
        }

        var vm = new DeviceViewModel(this, device);
        ApplyCustomization(vm);
        _devices[id] = vm;
        device.VolumeChanged += (_, e) => Post(() => OnDeviceVolumeChanged(vm, e));

        try
        {
            device.Sessions.SessionCreated += (_, _) => Post(() => RefreshSessions(vm));
            RefreshSessions(vm);
        }
        catch (COMException ex)
        {
            Log.Error($"セッション一覧を取得できません: {device.FriendlyName}", ex);
        }
    }

    private void RemoveDevice(string id)
    {
        if (!_devices.Remove(id, out var vm))
        {
            return;
        }

        foreach (var session in vm.Apps.SelectMany(a => a.Sessions).ToList())
        {
            _sessionOwners.Remove(session);
            _sessionInfo.Remove(session);
        }

        vm.Apps.Clear();
        vm.Device.Dispose();
    }

    private void OnDeviceActivated(string id)
    {
        AddDevice(id);
        RebuildLists();

        // Windows 自身の既定切り替えが済んでからルールを実行する
        RunLater(TimeSpan.FromMilliseconds(800), () => FireRules(new DeviceConnectedEvent(id)));
    }

    private void OnDeviceDeactivated(string id)
    {
        if (!_devices.ContainsKey(id))
        {
            return;
        }

        RemoveDevice(id);
        RebuildLists();
        RunLater(TimeSpan.FromMilliseconds(800), () => FireRules(new DeviceDisconnectedEvent(id)));
    }

    private void OnDefaultDeviceChanged(DefaultDeviceChangedEventArgs e)
    {
        switch (e.Flow, e.Role)
        {
            case (DeviceFlow.Render, AudioRole.Multimedia):
                DefaultOutputId = e.DeviceId;
                break;
            case (DeviceFlow.Capture, AudioRole.Multimedia):
                DefaultInputId = e.DeviceId;
                break;
            case (DeviceFlow.Render, AudioRole.Communications):
                CommunicationsOutputId = e.DeviceId;
                break;
            case (DeviceFlow.Capture, AudioRole.Communications):
                CommunicationsInputId = e.DeviceId;
                break;
            default:
                return;
        }

        UpdateDefaultFlags();
        RebuildLists();
        if (e.Flow == DeviceFlow.Render)
        {
            DefaultOutputStateChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DefaultInputStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDevicePropertyChanged(string id)
    {
        // 同じデバイスの通知がまとめて来るので、1 回にまとめて処理する
        if (!_pendingPropertyRefresh.Add(id))
        {
            return;
        }

        RunLater(TimeSpan.FromMilliseconds(300), () =>
        {
            _pendingPropertyRefresh.Remove(id);
            if (Find(id) is { } vm)
            {
                Safe(() =>
                {
                    vm.Device.RefreshProperties();
                    return 0;
                });
                vm.DeviceGlyph = Glyphs.ForDevice(vm.Device.FormFactor, vm.Flow);
                ApplyCustomization(vm);
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnDeviceVolumeChanged(DeviceViewModel vm, VolumeChangedEventArgs e)
    {
        if (!_devices.ContainsValue(vm))
        {
            return;
        }

        // 自分で変えた分はスライダーがすでに正しい位置にある（ドラッグ中に古い値で戻さないため反映しない）
        if (!e.IsSelf)
        {
            if (vm.Flow == DeviceFlow.Capture)
            {
                LogExternalCaptureChange(vm.Name, vm, vm.Volume, vm.IsMuted, e);
            }

            vm.SyncFromSystem(e.Volume, e.Muted);
        }

        RaiseDefaultStateChanged(vm);
    }

    private void ApplyCustomization(DeviceViewModel vm)
    {
        var custom = _settings.Current.Devices.GetValueOrDefault(vm.Id);
        vm.Name = string.IsNullOrWhiteSpace(custom?.DisplayName) ? vm.Device.Description : custom.DisplayName;
        vm.SubName = vm.Device.InterfaceName;
    }

    private void UpdateDefaultFlags()
    {
        foreach (var vm in _devices.Values)
        {
            var (defaultId, commId) = vm.Flow == DeviceFlow.Render
                ? (DefaultOutputId, CommunicationsOutputId)
                : (DefaultInputId, CommunicationsInputId);
            vm.IsDefault = string.Equals(vm.Id, defaultId, StringComparison.OrdinalIgnoreCase);
            vm.IsCommunicationsDefault = string.Equals(vm.Id, commId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RebuildLists(bool raiseEvent = true)
    {
        var general = _settings.Current.General;
        foreach (var (flow, target) in new[] { (DeviceFlow.Render, OutputDevices), (DeviceFlow.Capture, InputDevices) })
        {
            var desired = _devices.Values
                .Where(d => d.Flow == flow && (d.IsDefault || !IsHidden(d.Id)))
                .Where(d => !general.ShowOnlyDefaultDevice || d.IsDefault)
                .OrderByDescending(d => d.IsDefault)
                .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                .ToList();
            SyncCollection(target, desired);
        }

        if (raiseEvent)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- セッションの追従 ----

    private void RefreshAllSessions()
    {
        foreach (var vm in _devices.Values.ToList())
        {
            RefreshSessions(vm);
        }
    }

    private void RefreshSessions(DeviceViewModel vm)
    {
        if (!_devices.ContainsValue(vm))
        {
            return;
        }

        SessionRefreshResult result;
        try
        {
            result = vm.Device.Sessions.Refresh();
        }
        catch (COMException ex)
        {
            Log.Error($"セッションの更新に失敗: {vm.FullName}", ex);
            return;
        }

        foreach (var session in result.Removed)
        {
            _sessionInfo.Remove(session);
            _sessionOwners.Remove(session);
        }

        var needRouteRefresh = false;
        foreach (var session in result.Added)
        {
            var info = CreateSessionInfo(session);
            _sessionInfo[session] = info;
            session.VolumeChanged += (_, e) => Post(() => OnSessionVolumeChanged(session, e));
            session.StateChanged += (_, state) => Post(() => OnSessionStateChanged(session, state));
            session.Disconnected += (_, _) => Post(() => RefreshSessions(vm));

            if (!_initializing)
            {
                needRouteRefresh |= RestoreFromMemory(vm, session, info);
            }
        }

        RegroupDevice(vm);

        if (needRouteRefresh)
        {
            _refreshAllTimer.Stop();
            _refreshAllTimer.Start();
        }
    }

    /// <summary>デバイス上のセッションをアプリ単位にまとめ直し、画面の行を増減させる。</summary>
    private void RegroupDevice(DeviceViewModel vm)
    {
        var groups = new Dictionary<string, List<AudioSession>>(StringComparer.OrdinalIgnoreCase);
        var routedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in vm.Device.Sessions.Sessions)
        {
            if (!_sessionInfo.TryGetValue(session, out var info) || (vm.Flow == DeviceFlow.Capture && session.IsSystemSounds))
            {
                continue;
            }

            if (!session.IsSystemSounds && _routing.IsSupported)
            {
                // 別のデバイスに出力先を固定したアプリは、元のデバイスに残った古いセッションを出さない
                var routed = Safe(() => _routing.GetDevice(session.ProcessId, vm.Flow));
                if (routed is not null && _devices.ContainsKey(routed))
                {
                    if (!string.Equals(routed, vm.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    routedHere.Add(info.AppKey);
                }
            }

            if (!groups.TryGetValue(info.AppKey, out var list))
            {
                groups[info.AppKey] = list = [];
            }

            list.Add(session);
        }

        foreach (var app in vm.Apps.ToList())
        {
            if (!groups.ContainsKey(app.AppKey))
            {
                vm.Apps.Remove(app);
                foreach (var session in app.Sessions)
                {
                    _sessionOwners.Remove(session);
                }
            }
        }

        foreach (var (appKey, sessions) in groups)
        {
            var app = vm.Apps.FirstOrDefault(a => string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (app is null)
            {
                var info = _sessionInfo[sessions[0]];
                app = new AppViewModel(this, vm, appKey, info.DisplayName, info.ExecutablePath, sessions[0].IsSystemSounds);
                InsertSorted(vm.Apps, app);
                app.SyncFromSystem(Safe(() => sessions[0].Volume), Safe(() => sessions[0].Muted));
            }

            app.Sessions.Clear();
            app.Sessions.AddRange(sessions);
            foreach (var session in sessions)
            {
                _sessionOwners[session] = app;
            }

            var wasActive = app.IsActive;
            app.RefreshActive();
            app.RoutedDeviceName = routedHere.Contains(appKey) ? vm.Name : null;
            if (!_initializing && !wasActive && app.IsActive && vm.Flow == DeviceFlow.Render)
            {
                FireRules(new AppAudioStartedEvent(appKey));
            }
        }
    }

    private void OnSessionVolumeChanged(AudioSession session, VolumeChangedEventArgs e)
    {
        if (e.IsSelf || !_sessionOwners.TryGetValue(session, out var app))
        {
            return;
        }

        // Windows の音量ミキサーやアプリ自身が変えた
        if (app.Device.Flow == DeviceFlow.Capture)
        {
            // 入力側のセッション音量はマイク本体の音量と連動するので、本体と同じ値になっただけなら本体側のログで足りる
            if (Math.Round(e.Volume * 100) != app.Device.Volume)
            {
                LogExternalCaptureChange($"{app.DisplayName}（{app.Device.Name}）", app.Device, app.Volume, app.IsMuted, e);
            }

            app.SyncFromSystem(e.Volume, e.Muted);
            return;
        }

        app.SyncFromSystem(e.Volume, e.Muted);
        _ducking.ForgetSession(session.InstanceId);
        if (_memory.Record(app.AppKey, app.DisplayName, e.Volume, e.Muted))
        {
            _settings.ScheduleSave();
        }
    }

    private void OnSessionStateChanged(AudioSession session, AudioSessionState state)
    {
        if (!_sessionOwners.TryGetValue(session, out var app))
        {
            return;
        }

        if (state == AudioSessionState.Expired)
        {
            RefreshSessions(app.Device);
            return;
        }

        var wasActive = app.IsActive;
        app.RefreshActive();
        if (!wasActive && app.IsActive && app.Device.Flow == DeviceFlow.Render)
        {
            FireRules(new AppAudioStartedEvent(app.AppKey));
        }
    }

    /// <summary>覚えている音量などを新しいセッションに戻す。出力先を変えたら true。</summary>
    private bool RestoreFromMemory(DeviceViewModel vm, AudioSession session, SessionInfo info)
    {
        // 記憶は出力側だけのもの。同じアプリのマイク側に出力の音量を当てない
        if (vm.Flow == DeviceFlow.Capture || _memory.GetRestoreTarget(info.AppKey) is not { } entry)
        {
            return false;
        }

        try
        {
            if (Math.Abs(session.Volume - entry.Volume) > VolumeTolerance)
            {
                session.Volume = entry.Volume;
            }

            if (session.Muted != entry.Muted)
            {
                session.Muted = entry.Muted;
            }

            if (vm.Flow == DeviceFlow.Render
                && _memory.Settings.RestoreOutputDevice
                && entry.OutputDeviceId is { } target
                && _devices.ContainsKey(target)
                && !string.Equals(_routing.GetDevice(session.ProcessId, DeviceFlow.Render), target, StringComparison.OrdinalIgnoreCase))
            {
                return _routing.SetDevice(session.ProcessId, DeviceFlow.Render, target);
            }
        }
        catch (COMException ex)
        {
            Log.Error($"音量の復元に失敗: {info.DisplayName}", ex);
        }

        return false;
    }

    private SessionInfo CreateSessionInfo(AudioSession session)
    {
        if (session.IsSystemSounds)
        {
            return new SessionInfo(AppKeys.SystemSounds, "システム音", null);
        }

        var path = ProcessInfo.GetExecutablePath(session.ProcessId);
        if (path is not null)
        {
            var name = string.IsNullOrWhiteSpace(session.DisplayName) ? AppIconCache.GetDisplayName(path) : session.DisplayName;
            return new SessionInfo(AppKeys.FromExecutablePath(path), name, path);
        }

        // 保護されたプロセス（audiodg の「このデバイスを聴く」など）はパスが取れない
        var processName = GetProcessName(session.ProcessId);
        var display = string.IsNullOrWhiteSpace(session.DisplayName) ? processName : session.DisplayName;
        return new SessionInfo(AppKeys.FromProcessName($"{processName}|{display}"), display, null);
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"PID {processId}";
        }
    }

    private void RecordMemory(AppViewModel app)
    {
        // 記憶はアプリ単位で出力と入力を区別しないので、入力側の値は記録しない
        if (app.Device.Flow == DeviceFlow.Capture)
        {
            return;
        }

        if (_memory.Record(app.AppKey, app.DisplayName, (float)(app.Volume / 100), app.IsMuted))
        {
            _settings.ScheduleSave();
        }
    }

    /// <summary>
    /// 入力側の音量が SoundDeck 以外から変わったことをログに残す（勝手に変わる原因を追うため）。
    /// 変更元のアプリは OS から分からないので、そのときマイクを開いていたアプリを手がかりとして書く。
    /// </summary>
    private static void LogExternalCaptureChange(string target, DeviceViewModel device, double oldPercent, bool oldMuted, VolumeChangedEventArgs e)
    {
        var newPercent = Math.Round(e.Volume * 100);
        if (newPercent == oldPercent && e.Muted == oldMuted)
        {
            return;
        }

        var mute = e.Muted == oldMuted ? "" : $" ミュート: {(oldMuted ? "オン" : "オフ")} → {(e.Muted ? "オン" : "オフ")}";
        var apps = string.Join(", ", device.Apps.Select(a => a.IsActive ? $"{a.DisplayName}（録音中）" : a.DisplayName));
        Log.Info($"入力の音量が外部で変更: {target} {oldPercent}% → {newPercent}%{mute} (context={e.EventContext}) マイクを開いているアプリ: {(apps.Length == 0 ? "なし" : apps)}");
    }

    // ---- ルール ----

    private void FireRules(AudioEvent audioEvent)
    {
        foreach (var rule in _rules.Match(_settings.Current.Rules, audioEvent))
        {
            Log.Info($"ルールを実行: {rule.Name}");
            switch (rule.Action)
            {
                case RuleActionKind.SetDefaultOutput or RuleActionKind.SetDefaultInput when rule.ActionDeviceId is { } id && Find(id) is { } device:
                    SetDefaultDevice(id);
                    OsdRequested?.Invoke(this, new OsdMessage(Glyphs.Lightning, $"{rule.Name}: {device.Name}"));
                    break;
                case RuleActionKind.ApplyProfile when _settings.Current.Profiles.FirstOrDefault(p => p.Id == rule.ActionProfileId) is { } profile:
                    ApplyProfile(profile);
                    break;
            }
        }
    }

    // ---- ダッキング ----

    private void UpdateDuckingTimer()
    {
        var ducking = _settings.Current.Ducking;
        if (ducking.Enabled && ducking.TriggerApps.Count > 0)
        {
            _duckingTimer.Start();
        }
        else
        {
            _duckingTimer.Stop();
            if (_ducking.IsDucked)
            {
                ApplyVolumeChanges(_ducking.Restore(CollectLevels(out var lookup)), lookup);
            }
        }
    }

    private void OnDuckingTick()
    {
        var levels = CollectLevels(out var lookup);
        ApplyVolumeChanges(_ducking.Update(_settings.Current.Ducking, levels), lookup);
    }

    private List<SessionLevel> CollectLevels(out Dictionary<string, (AudioSession Session, AppViewModel App)> lookup)
    {
        lookup = [];
        var levels = new List<SessionLevel>();
        foreach (var app in _devices.Values.Where(d => d.Flow == DeviceFlow.Render).SelectMany(d => d.Apps))
        {
            foreach (var session in app.Sessions)
            {
                lookup[session.InstanceId] = (session, app);
                levels.Add(new SessionLevel(session.InstanceId, app.AppKey, Safe(session.GetPeak), Safe(() => session.Volume)));
            }
        }

        return levels;
    }

    private static void ApplyVolumeChanges(IReadOnlyList<VolumeChange> changes, Dictionary<string, (AudioSession Session, AppViewModel App)> lookup)
    {
        foreach (var change in changes)
        {
            if (lookup.TryGetValue(change.SessionId, out var target))
            {
                Safe(() =>
                {
                    target.Session.Volume = change.Volume;
                    return 0;
                });
                target.App.SyncFromSystem(change.Volume, target.App.IsMuted);
            }
        }
    }

    // ---- 小物 ----

    private void RaiseDefaultStateChanged(DeviceViewModel device)
    {
        if (device.IsDefault)
        {
            (device.Flow == DeviceFlow.Render ? DefaultOutputStateChanged : DefaultInputStateChanged)?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Post(Action action) => _dispatcher.TryEnqueue(() =>
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("音声イベントの処理に失敗", ex);
        }
    });

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            try
            {
                tick();
            }
            catch (Exception ex)
            {
                Log.Error("定期処理に失敗", ex);
            }
        };
        return timer;
    }

    private void RunLater(TimeSpan delay, Action action)
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Post(action);
        timer.Start();
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            Log.Error($"{what}に失敗", ex);
            OsdRequested?.Invoke(this, new OsdMessage(Glyphs.Info, $"{what}に失敗しました"));
        }
    }

    private static T Safe<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return default!;
        }
    }

    private static void InsertSorted(ObservableCollection<AppViewModel> apps, AppViewModel app)
    {
        static int Rank(AppViewModel a) => a.IsSystemSounds ? 1 : 0;

        var index = 0;
        while (index < apps.Count
            && (Rank(apps[index]) < Rank(app)
                || (Rank(apps[index]) == Rank(app) && string.Compare(apps[index].DisplayName, app.DisplayName, StringComparison.CurrentCulture) <= 0)))
        {
            index++;
        }

        apps.Insert(index, app);
    }

    /// <summary>画面のちらつきを抑えるため、作り直さずに差分だけ動かす。</summary>
    private static void SyncCollection<T>(ObservableCollection<T> target, IList<T> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var current = target.IndexOf(desired[i]);
            if (current < 0)
            {
                target.Insert(i, desired[i]);
            }
            else if (current != i)
            {
                target.Move(current, i);
            }
        }
    }

    private sealed record SessionInfo(string AppKey, string DisplayName, string? ExecutablePath);
}
