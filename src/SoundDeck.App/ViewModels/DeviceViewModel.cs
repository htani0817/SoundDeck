using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundDeck.App.Services;
using SoundDeck.Audio;
using SoundDeck.Core.Commands;

namespace SoundDeck.App.ViewModels;

/// <summary>フライアウトのデバイス 1 枚分。</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    private readonly AudioService _service;
    private bool _syncing;

    internal DeviceViewModel(AudioService service, AudioDevice device)
    {
        _service = service;
        Device = device;
        Id = device.Id;
        Flow = device.Flow;
        DeviceGlyph = Glyphs.ForDevice(device.FormFactor, device.Flow);
        Name = device.Description;
        SubName = device.InterfaceName;
        SyncFromSystem(device.Volume, device.Muted);
        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasApps));
            OnPropertyChanged(nameof(AppCountText));
        };
    }

    internal AudioDevice Device { get; }

    public string Id { get; }

    public DeviceFlow Flow { get; }

    public bool IsCapture => Flow == DeviceFlow.Capture;

    public ObservableCollection<AppViewModel> Apps { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string SubName { get; set; }

    [ObservableProperty]
    public partial string DeviceGlyph { get; set; }

    [ObservableProperty]
    public partial bool IsDefault { get; set; }

    /// <summary>アプリの一覧を開いているか。既定のデバイスは開き、それ以外は閉じておく。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    public partial bool IsExpanded { get; set; }

    public bool HasApps => Apps.Count > 0;

    public string AppCountText => Apps.Count.ToString();

    public string ExpandGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    [ObservableProperty]
    public partial bool IsCommunicationsDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    public partial double Volume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial double Peak { get; set; }

    public string VolumeGlyph => IsCapture ? Glyphs.ForMicrophone(IsMuted) : Glyphs.ForVolume(Volume, IsMuted);

    public string FullName => string.IsNullOrEmpty(SubName) ? Name : $"{Name} ({SubName})";

    /// <summary>OS 側の値を画面に反映する（このときは OS へ書き戻さない）。</summary>
    internal void SyncFromSystem(float volume, bool muted)
    {
        _syncing = true;
        Volume = Math.Round(volume * 100);
        IsMuted = muted;
        _syncing = false;
    }

    internal void UpdatePeak(float peak)
    {
        // 針がすぐ落ちないよう、下がるときだけゆっくりにする
        var target = IsMuted ? 0 : peak * Volume / 100.0;
        Peak = target >= Peak ? target : Math.Max(target, Peak - 0.04);
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_syncing)
        {
            _service.SetDeviceVolume(this, (float)(value / 100.0), fromUi: true);
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_syncing)
        {
            _service.SetDeviceMute(this, value, fromUi: true);
        }
    }

    partial void OnIsDefaultChanged(bool value)
    {
        if (value)
        {
            IsExpanded = true;
        }
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(FullName));

    partial void OnSubNameChanged(string value) => OnPropertyChanged(nameof(FullName));
}
