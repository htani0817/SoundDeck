using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Profiles;

namespace SoundDeck.App.Views.Settings;

public sealed partial class ProfilesPage : SettingsPageBase
{
    protected override string PageTitle => "プロファイル";

    protected override void Build()
    {
        AddNote("既定のデバイス、デバイスの音量、アプリごとの音量と出力先をまとめて保存し、ワンクリックやホットキー、自動切替ルールで呼び出せます。");

        var nameBox = new TextBox { PlaceholderText = "新しいプロファイルの名前", Width = 240 };
        AddCard("今の状態を新しいプロファイルとして保存", "フライアウト下の「プロファイル」からも保存できます。", Glyphs.Add, Row(nameBox, TextButton("保存", () =>
        {
            var name = nameBox.Text.Trim();
            Current.Profiles.Add(ProfilePlanner.Capture(name.Length > 0 ? name : $"プロファイル {Current.Profiles.Count + 1}", Audio.GetSnapshot()));
            Save();
            Rebuild();
        }, accent: true)));

        AddSection($"保存したプロファイル（{Current.Profiles.Count}）");
        foreach (var profile in Current.Profiles.ToList())
        {
            AddProfileCard(profile);
        }
    }

    private void AddProfileCard(Profile profile)
    {
        var rename = new TextBox { Text = profile.Name, Width = 200 };
        rename.LostFocus += (_, _) =>
        {
            var name = rename.Text.Trim();
            if (name.Length > 0 && name != profile.Name)
            {
                profile.Name = name;
                Save();
            }
        };

        var commands = Row(
            rename,
            TextButton("適用", () => Audio.ApplyProfile(profile), accent: true),
            new Button
            {
                Content = new FontIcon { Glyph = Glyphs.More, FontSize = 14 },
                Flyout = CreateMenu(profile),
            });

        AddCard(profile.Name, Describe(profile), Glyphs.Sliders, commands);
    }

    private MenuFlyout CreateMenu(Profile profile)
    {
        var menu = new MenuFlyout();
        var overwrite = new MenuFlyoutItem { Text = "今の状態で上書き", Icon = new FontIcon { Glyph = "\uE74E" } };
        overwrite.Click += (_, _) =>
        {
            var captured = ProfilePlanner.Capture(profile.Name, Audio.GetSnapshot());
            profile.DefaultOutputId = captured.DefaultOutputId;
            profile.DefaultInputId = captured.DefaultInputId;
            profile.Devices = captured.Devices;
            profile.Apps = captured.Apps;
            Save();
            Rebuild();
        };
        menu.Items.Add(overwrite);

        var delete = new MenuFlyoutItem { Text = "削除", Icon = new FontIcon { Glyph = Glyphs.Delete } };
        delete.Click += (_, _) =>
        {
            Current.Profiles.Remove(profile);
            Save();
            Rebuild();
        };
        menu.Items.Add(delete);
        return menu;
    }

    private static string Describe(Profile profile)
    {
        var parts = new List<string>();
        if (profile.DefaultOutputId is { } output)
        {
            parts.Add($"出力: {DeviceName(output)}");
        }

        if (profile.DefaultInputId is { } input)
        {
            parts.Add($"入力: {DeviceName(input)}");
        }

        parts.Add($"アプリ {profile.Apps.Count} 件");
        var apps = string.Join("、", profile.Apps.Take(4).Select(a => a.Muted == true ? $"{a.DisplayName}(ミュート)" : $"{a.DisplayName} {a.Volume * 100:0}%"));
        return string.Join(" ・ ", parts) + (apps.Length > 0 ? $"\n{apps}{(profile.Apps.Count > 4 ? " ほか" : "")}" : "");
    }

    private static string DeviceName(string id) =>
        Audio.Find(id)?.FullName
        ?? Audio.GetDeviceSummaries(Core.Commands.DeviceFlow.Render).Concat(Audio.GetDeviceSummaries(Core.Commands.DeviceFlow.Capture))
            .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "（見つからないデバイス）";
}
