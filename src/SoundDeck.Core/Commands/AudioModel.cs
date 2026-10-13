namespace SoundDeck.Core.Commands;

/// <summary>音声デバイスの向き（再生か録音か）。</summary>
public enum DeviceFlow
{
    Render,
    Capture,
}

/// <summary>アプリを識別するキーの作り方。実行ファイルのフルパスを小文字にしたものを使う。</summary>
public static class AppKeys
{
    /// <summary>Windows のシステム音セッション用の固定キー。</summary>
    public const string SystemSounds = "#system-sounds";

    public static string FromExecutablePath(string path) => Path.GetFullPath(path).ToLowerInvariant();

    /// <summary>実行ファイルのパスが取れないプロセス（権限不足など）向けの代替キー。</summary>
    public static string FromProcessName(string processName) => "#process:" + processName.ToLowerInvariant();
}

/// <summary>ある時点の音声状態。プロファイルの保存・適用の元になる。</summary>
public sealed record AudioSnapshot(
    string? DefaultOutputId,
    string? DefaultInputId,
    IReadOnlyList<DeviceAudioState> Devices,
    IReadOnlyList<AppAudioState> Apps);

public sealed record DeviceAudioState(string DeviceId, DeviceFlow Flow, float Volume, bool Muted);

/// <summary>アプリ単位の状態。同じアプリのセッションが複数あっても 1 件にまとめる。</summary>
public sealed record AppAudioState(string AppKey, string DisplayName, float Volume, bool Muted, string? OutputDeviceId);

/// <summary>音声状態を変える指示。Core が組み立て、App 側が実際の API 呼び出しに変換する。</summary>
public abstract record AudioCommand;

public sealed record SetDefaultDeviceCommand(string DeviceId, DeviceFlow Flow) : AudioCommand;

public sealed record SetDeviceVolumeCommand(string DeviceId, float? Volume, bool? Muted) : AudioCommand;

/// <summary>アプリの音量・ミュート・出力先。起動していないアプリは「次に起動したときの復元値」として扱う。</summary>
public sealed record SetAppStateCommand(
    string AppKey,
    string DisplayName,
    float? Volume,
    bool? Muted,
    bool SetOutput,
    string? OutputDeviceId) : AudioCommand;
