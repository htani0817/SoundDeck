using System.Text;

namespace SoundDeck.App.Services;

/// <summary>%LOCALAPPDATA%\SoundDeck\logs にエラーを残す簡単なログ。</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock s_lock = new();

    public static string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "logs", "sounddeck.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{(ex is null ? "" : Environment.NewLine + ex)}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
        }
        catch (IOException)
        {
            // ログが書けなくてもアプリは止めない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundDeck");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
}
