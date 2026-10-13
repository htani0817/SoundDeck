using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;

namespace SoundDeck.App.Views.Settings;

public sealed partial class AboutPage : SettingsPageBase
{
    protected override string PageTitle => "バージョン情報";

    protected override void Build()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        AddCard("SoundDeck", $"バージョン {version}\nWindows 11 向けの音量ミキサー", Glyphs.Volume3, null);

        AddSection("データの場所");
        AddCard("設定とログのフォルダー", AppPaths.DataDirectory, "\uE8B7", TextButton("開く", () =>
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            _ = Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.DataDirectory);
        }));

        AddSection("終了");
        AddCard("SoundDeck を終了する", "通知領域のアイコンも消えます。ダッキングで下げていた音量は元に戻します。", "\uE7E8", TextButton("終了", () => App.Current.Quit()));

        AddSection("注意");
        AddNote("既定デバイスの切り替えとアプリ別の出力先は、Windows が公開していない機能を使っています。Windows の更新で動かなくなった場合、その操作だけが無効になり、ほかの機能はそのまま使えます。");
    }
}
