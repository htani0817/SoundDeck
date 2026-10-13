using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SoundDeck.App.Services;
using SoundDeck.Audio;

namespace SoundDeck.App.ViewModels;

/// <summary>
/// デバイス上の 1 アプリ分。Chrome のように同じアプリが複数のセッションを持つ場合は 1 行にまとめ、音量はまとめて変える。
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    private readonly AudioService _service;
    private bool _syncing;

    internal AppViewModel(AudioService service, DeviceViewModel device, string appKey, string displayName, string? executablePath, bool isSystemSounds)
    {
        _service = service;
        Device = device;
        AppKey = appKey;
        DisplayName = displayName;
        ExecutablePath = executablePath;
        IsSystemSounds = isSystemSounds;
        Icon = AppIconCache.GetIcon(executablePath);
    }

    public DeviceViewModel Device { get; }

    public string AppKey { get; }

    public string? ExecutablePath { get; }

    public bool IsSystemSounds { get; }

    internal List<AudioSession> Sessions { get; } = [];

    public string FallbackGlyph => IsSystemSounds ? Glyphs.Volume : Glyphs.App;

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial double Peak { get; set; }

    /// <summary>いずれかのセッションが音を出している状態か。</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>アプリ別の出力先が指定されているときのデバイス名（既定に従うなら null）。</summary>
    [ObservableProperty]
    public partial string? RoutedDeviceName { get; set; }

    internal IEnumerable<uint> ProcessIds => Sessions.Select(s => s.ProcessId).Where(id => id != 0).Distinct();

    internal void SyncFromSystem(float volume, bool muted)
    {
        _syncing = true;
        Volume = Math.Round(volume * 100);
        IsMuted = muted;
        _syncing = false;
    }

    internal void UpdatePeak()
    {
        var peak = 0f;
        foreach (var session in Sessions)
        {
            peak = Math.Max(peak, session.GetPeak());
        }

        var target = IsMuted ? 0 : peak * Volume / 100.0;
        Peak = target >= Peak ? target : Math.Max(target, Peak - 0.04);
    }

    internal void RefreshActive() => IsActive = Sessions.Any(s => s.State == AudioSessionState.Active);

    partial void OnVolumeChanged(double value)
    {
        if (!_syncing)
        {
            _service.SetAppVolume(this, (float)(value / 100.0), fromUi: true);
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_syncing)
        {
            _service.SetAppMute(this, value, fromUi: true);
        }
    }
}
