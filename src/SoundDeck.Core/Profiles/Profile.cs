using SoundDeck.Core.Commands;

namespace SoundDeck.Core.Profiles;

/// <summary>既定デバイス・デバイス音量・アプリ別の音量と出力先をまとめて保存したもの。</summary>
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string? DefaultOutputId { get; set; }

    public string? DefaultInputId { get; set; }

    public List<ProfileDeviceEntry> Devices { get; set; } = [];

    public List<ProfileAppEntry> Apps { get; set; } = [];
}

public sealed class ProfileDeviceEntry
{
    public string DeviceId { get; set; } = "";

    public float? Volume { get; set; }

    public bool? Muted { get; set; }
}

public sealed class ProfileAppEntry
{
    public string AppKey { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public float? Volume { get; set; }

    public bool? Muted { get; set; }

    /// <summary>null は「Windows の既定に従う」。</summary>
    public string? OutputDeviceId { get; set; }
}

public static class ProfilePlanner
{
    private const float VolumeTolerance = 0.005f;

    /// <summary>今の状態をそのままプロファイルにする。</summary>
    public static Profile Capture(string name, AudioSnapshot snapshot) => new()
    {
        Name = name,
        DefaultOutputId = snapshot.DefaultOutputId,
        DefaultInputId = snapshot.DefaultInputId,
        Devices = [.. snapshot.Devices.Select(d => new ProfileDeviceEntry { DeviceId = d.DeviceId, Volume = d.Volume, Muted = d.Muted })],
        Apps = [.. snapshot.Apps
            .Where(a => a.AppKey != AppKeys.SystemSounds)
            .Select(a => new ProfileAppEntry
            {
                AppKey = a.AppKey,
                DisplayName = a.DisplayName,
                Volume = a.Volume,
                Muted = a.Muted,
                OutputDeviceId = a.OutputDeviceId,
            })],
    };

    /// <summary>
    /// プロファイルを今の状態へ当てるための指示を組み立てる。
    /// すでに同じ値のものは省く。接続されていないデバイスは飛ばす。
    /// 起動していないアプリの指示も出す（App 側で次回起動時の復元値として保存する）。
    /// </summary>
    public static IReadOnlyList<AudioCommand> Plan(Profile profile, AudioSnapshot current)
    {
        var commands = new List<AudioCommand>();
        var devices = current.Devices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);

        if (profile.DefaultOutputId is { } output
            && !string.Equals(output, current.DefaultOutputId, StringComparison.OrdinalIgnoreCase)
            && devices.TryGetValue(output, out var outDevice) && outDevice.Flow == DeviceFlow.Render)
        {
            commands.Add(new SetDefaultDeviceCommand(output, DeviceFlow.Render));
        }

        if (profile.DefaultInputId is { } input
            && !string.Equals(input, current.DefaultInputId, StringComparison.OrdinalIgnoreCase)
            && devices.TryGetValue(input, out var inDevice) && inDevice.Flow == DeviceFlow.Capture)
        {
            commands.Add(new SetDefaultDeviceCommand(input, DeviceFlow.Capture));
        }

        foreach (var entry in profile.Devices)
        {
            if (!devices.TryGetValue(entry.DeviceId, out var state))
            {
                continue;
            }

            var volume = entry.Volume is { } v && Math.Abs(v - state.Volume) > VolumeTolerance ? v : (float?)null;
            var muted = entry.Muted is { } m && m != state.Muted ? m : (bool?)null;
            if (volume is not null || muted is not null)
            {
                commands.Add(new SetDeviceVolumeCommand(entry.DeviceId, volume, muted));
            }
        }

        var apps = current.Apps.ToDictionary(a => a.AppKey, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Apps)
        {
            // 出力先に指定したデバイスが今は無いなら、出力先は触らない
            var outputAvailable = entry.OutputDeviceId is null || devices.ContainsKey(entry.OutputDeviceId);

            if (!apps.TryGetValue(entry.AppKey, out var state))
            {
                commands.Add(new SetAppStateCommand(entry.AppKey, entry.DisplayName, entry.Volume, entry.Muted, outputAvailable, entry.OutputDeviceId));
                continue;
            }

            var volume = entry.Volume is { } v && Math.Abs(v - state.Volume) > VolumeTolerance ? v : (float?)null;
            var muted = entry.Muted is { } m && m != state.Muted ? m : (bool?)null;
            var setOutput = outputAvailable && !string.Equals(entry.OutputDeviceId, state.OutputDeviceId, StringComparison.OrdinalIgnoreCase);
            if (volume is not null || muted is not null || setOutput)
            {
                commands.Add(new SetAppStateCommand(entry.AppKey, entry.DisplayName, volume, muted, setOutput, entry.OutputDeviceId));
            }
        }

        return commands;
    }
}
