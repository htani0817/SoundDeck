using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundDeck.Core.Settings;

/// <summary>設定を JSON ファイルへ読み書きする。壊れたファイルは退避して既定値で起動する。</summary>
public sealed class SettingsStore(string path)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = path;

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            // 手で壊した設定でも起動できるよう、元ファイルを残して既定値に戻す
            var backup = FilePath + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(FilePath, backup, overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // 書き込み途中で落ちても元の設定が消えないよう、一時ファイル経由で置き換える
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, FilePath, overwrite: true);
    }
}
