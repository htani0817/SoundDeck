using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.ViewModels;

namespace SoundDeck.App.Views.Settings;

public sealed partial class MemoryPage : SettingsPageBase
{
    protected override string PageTitle => "アプリ音量の記憶";

    protected override void Build()
    {
        var memory = Current.VolumeMemory;

        AddNote("アプリごとの音量・ミュート・出力先を覚えておき、そのアプリが次に音を出したときに自動で戻します。");
        AddCard("アプリの音量を覚えて戻す", "SoundDeck や Windows の音量ミキサーで変えた音量を記録します。", Glyphs.History, Toggle(memory.Enabled, on =>
        {
            memory.Enabled = on;
            Save();
        }));
        AddCard("出力先のデバイスも戻す", "アプリごとに選んだ出力先を、アプリを起動し直したときにも適用します。", Glyphs.Switch, Toggle(memory.RestoreOutputDevice, on =>
        {
            memory.RestoreOutputDevice = on;
            Save();
        }));

        AddSection($"覚えているアプリ（{memory.Apps.Count}）");
        if (memory.Apps.Count == 0)
        {
            AddNote("まだありません。フライアウトでアプリの音量を変えると、ここに追加されます。");
        }

        foreach (var (appKey, entry) in memory.Apps.OrderBy(p => p.Value.DisplayName, StringComparer.CurrentCulture))
        {
            var detail = entry.Muted ? "ミュート" : $"音量 {entry.Volume * 100:0}%";
            if (entry.OutputDeviceId is { } deviceId)
            {
                detail += $" ・ 出力先: {Audio.Find(deviceId)?.FullName ?? "（未接続のデバイス）"}";
            }

            var card = AddCard(entry.DisplayName, $"{detail}\n{appKey}", Glyphs.App, Row(
                TextButton("覚えない", () =>
                {
                    Audio.Memory.SetExcluded(appKey, true);
                    Save();
                    Rebuild();
                }),
                IconButton(Glyphs.Delete, "記録を消す", () =>
                {
                    Audio.Memory.Forget(appKey);
                    Save();
                    Rebuild();
                })));
            ToolTipService.SetToolTip(card, appKey);
        }

        AddSection($"覚えないアプリ（{memory.ExcludedApps.Count}）");
        if (memory.ExcludedApps.Count == 0)
        {
            AddNote("ありません。フライアウトのアプリ横の「…」からも指定できます。");
        }

        foreach (var appKey in memory.ExcludedApps.ToList())
        {
            AddCard(Path.GetFileName(appKey), appKey, "\uE711", TextButton("覚えるように戻す", () =>
            {
                Audio.Memory.SetExcluded(appKey, false);
                Save();
                Rebuild();
            }));
        }
    }
}
