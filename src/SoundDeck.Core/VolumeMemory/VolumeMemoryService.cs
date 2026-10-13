using SoundDeck.Core.Commands;

namespace SoundDeck.Core.VolumeMemory;

public sealed class VolumeMemorySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>出力先デバイスも覚えて戻す。</summary>
    public bool RestoreOutputDevice { get; set; } = true;

    /// <summary>キーはアプリキー。</summary>
    public Dictionary<string, AppVolumeEntry> Apps { get; set; } = [];

    /// <summary>記憶・復元しないアプリのキー。</summary>
    public List<string> ExcludedApps { get; set; } = [];
}

public sealed class AppVolumeEntry
{
    public string DisplayName { get; set; } = "";

    public float Volume { get; set; } = 1f;

    public bool Muted { get; set; }

    /// <summary>null は「Windows の既定に従う」。</summary>
    public string? OutputDeviceId { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>アプリごとの音量・ミュート・出力先を覚え、次にそのアプリが音を出したときに戻す。</summary>
public sealed class VolumeMemoryService(VolumeMemorySettings settings, TimeProvider time)
{
    private const float VolumeTolerance = 0.005f;

    public VolumeMemorySettings Settings => settings;

    public bool IsExcluded(string appKey) =>
        settings.ExcludedApps.Contains(appKey, StringComparer.OrdinalIgnoreCase);

    public bool IsTracked(string appKey) =>
        settings.Enabled && appKey != AppKeys.SystemSounds && !IsExcluded(appKey);

    /// <summary>セッションが現れたときに戻すべき値。覚えていなければ null。</summary>
    public AppVolumeEntry? GetRestoreTarget(string appKey) =>
        IsTracked(appKey) && settings.Apps.TryGetValue(appKey, out var entry) ? entry : null;

    /// <summary>音量とミュートを記録する。値が変わったら true（保存のきっかけに使う）。</summary>
    public bool Record(string appKey, string displayName, float volume, bool muted)
    {
        if (!IsTracked(appKey))
        {
            return false;
        }

        var entry = GetOrCreate(appKey, displayName);
        if (Math.Abs(entry.Volume - volume) <= VolumeTolerance && entry.Muted == muted && entry.DisplayName == displayName)
        {
            return false;
        }

        entry.Volume = volume;
        entry.Muted = muted;
        entry.DisplayName = displayName;
        entry.LastUpdated = time.GetUtcNow();
        return true;
    }

    public bool RecordOutput(string appKey, string displayName, string? deviceId)
    {
        if (!IsTracked(appKey) || !settings.RestoreOutputDevice)
        {
            return false;
        }

        var entry = GetOrCreate(appKey, displayName);
        if (string.Equals(entry.OutputDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        entry.OutputDeviceId = deviceId;
        entry.LastUpdated = time.GetUtcNow();
        return true;
    }

    /// <summary>プロファイル適用などで、起動していないアプリの次回値を書き込む。除外中でも書かない。</summary>
    public bool Apply(SetAppStateCommand command)
    {
        if (!IsTracked(command.AppKey))
        {
            return false;
        }

        var entry = GetOrCreate(command.AppKey, command.DisplayName);
        if (command.Volume is { } volume)
        {
            entry.Volume = volume;
        }

        if (command.Muted is { } muted)
        {
            entry.Muted = muted;
        }

        if (command.SetOutput && settings.RestoreOutputDevice)
        {
            entry.OutputDeviceId = command.OutputDeviceId;
        }

        entry.LastUpdated = time.GetUtcNow();
        return true;
    }

    public void SetExcluded(string appKey, bool excluded)
    {
        settings.ExcludedApps.RemoveAll(k => string.Equals(k, appKey, StringComparison.OrdinalIgnoreCase));
        if (excluded)
        {
            settings.ExcludedApps.Add(appKey);
            settings.Apps.Remove(appKey);
        }
    }

    public bool Forget(string appKey) => settings.Apps.Remove(appKey);

    private AppVolumeEntry GetOrCreate(string appKey, string displayName)
    {
        if (!settings.Apps.TryGetValue(appKey, out var entry))
        {
            entry = new AppVolumeEntry { DisplayName = displayName, LastUpdated = time.GetUtcNow() };
            settings.Apps[appKey] = entry;
        }

        return entry;
    }
}
