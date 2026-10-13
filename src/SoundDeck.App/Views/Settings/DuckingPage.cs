using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.ViewModels;

namespace SoundDeck.App.Views.Settings;

public sealed partial class DuckingPage : SettingsPageBase
{
    protected override string PageTitle => "ダッキング";

    protected override void Build()
    {
        var ducking = Current.Ducking;

        AddNote("指定したアプリ（通話アプリなど）が音を出している間だけ、ほかのアプリの音量を自動で下げます。止まって少したつと元に戻ります。");

        AddCard("ダッキングを使う", "フライアウトのアプリ横の「…」→「このアプリが鳴ったら他を下げる」でも指定できます。", Glyphs.Volume1, Toggle(ducking.Enabled, on =>
        {
            ducking.Enabled = on;
            Save();
        }));

        var levelText = new TextBlock { Text = $"{ducking.DuckLevelPercent}%", MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
        var level = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5, Value = ducking.DuckLevelPercent, Width = 220 };
        level.ValueChanged += (_, e) =>
        {
            ducking.DuckLevelPercent = (int)e.NewValue;
            levelText.Text = $"{ducking.DuckLevelPercent}%";
            SettingsService.ScheduleSave();
        };
        AddCard("下げている間の音量", "ほかのアプリを、元の音量の何 % にするか。0% で無音になります。", Glyphs.Volume0, Row(level, levelText));

        var thresholdText = new TextBlock { Text = $"{ducking.Threshold * 100:0.#}%", MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
        var threshold = new Slider { Minimum = 0.5, Maximum = 20, StepFrequency = 0.5, Value = ducking.Threshold * 100, Width = 220 };
        threshold.ValueChanged += (_, e) =>
        {
            ducking.Threshold = (float)(e.NewValue / 100);
            thresholdText.Text = $"{e.NewValue:0.#}%";
            SettingsService.ScheduleSave();
        };
        AddCard("「鳴っている」とみなす大きさ", "小さなノイズで反応してしまうときは上げてください。", Glyphs.Sliders, Row(threshold, thresholdText));

        var delay = new NumberBox
        {
            Minimum = 0,
            Maximum = 60000,
            SmallChange = 250,
            LargeChange = 1000,
            Value = ducking.ReleaseDelayMs,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 140,
        };
        delay.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue))
            {
                ducking.ReleaseDelayMs = (int)e.NewValue;
                SettingsService.ScheduleSave();
            }
        };
        AddCard("元に戻すまでの待ち時間（ミリ秒）", "会話の切れ目ごとに音量が上下しないよう、少し待ってから戻します。", "\uE916", delay);

        AddSection("きっかけにするアプリ");
        var candidates = Audio.GetRunningApps()
            .Where(a => !ducking.TriggerApps.Contains(a.AppKey, StringComparer.OrdinalIgnoreCase))
            .Select(a => (a.DisplayName, a.AppKey))
            .ToList();
        var picker = Choice(candidates, null, _ => { }, minWidth: 260);
        picker.PlaceholderText = candidates.Count == 0 ? "音を出しているアプリがありません" : "いま動いているアプリから選ぶ";
        AddCard("アプリを追加", "", Glyphs.Add, Row(picker, TextButton("追加", () =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string key })
            {
                ducking.TriggerApps.Add(key);
                Save();
                Rebuild();
            }
        })));

        if (ducking.TriggerApps.Count == 0)
        {
            AddNote("まだありません。");
        }

        foreach (var key in ducking.TriggerApps.ToList())
        {
            var name = Audio.GetRunningApps().FirstOrDefault(a => string.Equals(a.AppKey, key, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? Current.VolumeMemory.Apps.GetValueOrDefault(key)?.DisplayName
                ?? Path.GetFileName(key);
            AddCard(name, key, Glyphs.App, IconButton(Glyphs.Delete, "外す", () =>
            {
                ducking.TriggerApps.Remove(key);
                Save();
                Rebuild();
            }));
        }
    }
}
