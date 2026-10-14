using SoundDeck.Core.Ducking;
using SoundDeck.Core.Hotkeys;
using SoundDeck.Core.Profiles;
using SoundDeck.Core.Rules;
using SoundDeck.Core.VolumeMemory;

namespace SoundDeck.Core.Settings;

/// <summary>settings.json に保存する設定全体。</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>初回起動の案内を出したか。</summary>
    public bool WelcomeShown { get; set; }

    public GeneralSettings General { get; set; } = new();

    public List<HotkeyBinding> Hotkeys { get; set; } = HotkeyBinding.CreateDefaults();

    /// <summary>キーはデバイス ID。表示名の変更や非表示の指定を持つ。</summary>
    public Dictionary<string, DeviceCustomization> Devices { get; set; } = [];

    public VolumeMemorySettings VolumeMemory { get; set; } = new();

    public List<Profile> Profiles { get; set; } = [];

    public List<AutomationRule> Rules { get; set; } = [];

    public DuckingSettings Ducking { get; set; } = new();

    /// <summary>JSON から読んだ直後に null を既定値へ戻す。</summary>
    public void Normalize()
    {
        General ??= new();
        Hotkeys ??= [];
        Devices ??= [];
        VolumeMemory ??= new();
        VolumeMemory.Apps ??= [];
        VolumeMemory.ExcludedApps ??= [];
        Profiles ??= [];
        foreach (var profile in Profiles)
        {
            profile.Devices ??= [];
            profile.Apps ??= [];
        }

        Rules ??= [];
        Ducking ??= new();
        Ducking.TriggerApps ??= [];
        Ducking.DuckLevelPercent = Math.Clamp(Ducking.DuckLevelPercent, 0, 100);
        Ducking.ReleaseDelayMs = Math.Clamp(Ducking.ReleaseDelayMs, 0, 60_000);
        General.ScrollStepPercent = Math.Clamp(General.ScrollStepPercent, 1, 20);
        if (!Enum.IsDefined(General.Theme))
        {
            General.Theme = AppTheme.System;
        }
    }
}

/// <summary>画面の配色。</summary>
public enum AppTheme
{
    /// <summary>Windows の設定（アプリ モード）に合わせる。</summary>
    System,

    /// <summary>白い背景。</summary>
    Light,

    /// <summary>黒い背景。</summary>
    Dark,
}

public sealed class GeneralSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool LaunchAtStartup { get; set; }

    public bool ShowPeakMeters { get; set; } = true;

    /// <summary>フライアウトに既定デバイスだけを出す。</summary>
    public bool ShowOnlyDefaultDevice { get; set; }

    /// <summary>既定デバイスを切り替えるとき、通信デバイスも一緒に切り替える。</summary>
    public bool SwitchCommunicationsDevice { get; set; } = true;

    /// <summary>ホットキー操作時に画面下へ OSD を出す。</summary>
    public bool ShowOsd { get; set; } = true;

    /// <summary>トレイアイコン上のホイール 1 段で変わる音量（%）。</summary>
    public int ScrollStepPercent { get; set; } = 2;
}

public sealed class DeviceCustomization
{
    public string? DisplayName { get; set; }

    public bool Hidden { get; set; }
}
