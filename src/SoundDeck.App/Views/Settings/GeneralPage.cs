using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;

namespace SoundDeck.App.Views.Settings;

public sealed partial class GeneralPage : SettingsPageBase
{
    protected override string PageTitle => "一般";

    protected override void Build()
    {
        var general = Current.General;

        AddSection("起動");
        AddCard("Windows の起動時に SoundDeck を開始する", "サインインすると通知領域に常駐します。", "\uE7E8", Toggle(general.LaunchAtStartup, on =>
        {
            general.LaunchAtStartup = on;
            try
            {
                StartupRegistration.Set(on);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Log.Error("自動起動の登録に失敗", ex);
            }

            Save();
        }));

        AddSection("フライアウト");
        AddCard("ピークメーターを表示する", "音量スライダーの下に、今鳴っている音の大きさを表示します。", Glyphs.Sliders, Toggle(general.ShowPeakMeters, on =>
        {
            general.ShowPeakMeters = on;
            Save();
        }));
        AddCard("既定のデバイスだけを表示する", "オフにすると、つながっているすべてのデバイスを並べます（隠したデバイスを除く）。", Glyphs.Speakers, Toggle(general.ShowOnlyDefaultDevice, on =>
        {
            general.ShowOnlyDefaultDevice = on;
            Save();
        }));

        var step = new NumberBox
        {
            Minimum = 1,
            Maximum = 20,
            Value = general.ScrollStepPercent,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 120,
        };
        step.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue))
            {
                general.ScrollStepPercent = (int)args.NewValue;
                Save();
            }
        };
        AddCard("ホイール 1 段で変える音量（%）", "トレイアイコンやスライダーの上でホイールを回したとき、音量ホットキーのときに使います。", "\uE962", step);

        AddSection("動作");
        AddCard("既定デバイスを切り替えるとき、通信デバイスも切り替える", "Teams や Discord などの通話アプリが使うデバイスも一緒に切り替えます。", Glyphs.Headset, Toggle(general.SwitchCommunicationsDevice, on =>
        {
            general.SwitchCommunicationsDevice = on;
            Save();
        }));
        AddCard("画面下に通知（OSD）を表示する", "ホットキーやルールでマイクのミュート・音量・プロファイルが変わったときに表示します。", "\uE7F4", Toggle(general.ShowOsd, on =>
        {
            general.ShowOsd = on;
            Save();
        }));

        AddSection("この PC での対応状況");
        AddCard(
            "アプリごとの出力先の切り替え",
            Audio.IsAppRoutingSupported
                ? "使えます。フライアウトのアプリ横の「…」から出力先を選べます。"
                : "この Windows では使えません（非公開 API が見つかりませんでした）。",
            Glyphs.Switch,
            new TextBlock { Text = Audio.IsAppRoutingSupported ? "対応" : "非対応" });
    }
}
