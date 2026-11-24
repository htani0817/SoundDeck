using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Views.Settings;

public sealed partial class GeneralPage : SettingsPageBase
{
    private static readonly (string Label, AppTheme Value)[] s_themes =
    [
        ("システムに合わせる", AppTheme.System),
        ("ダーク", AppTheme.Dark),
        ("ライト", AppTheme.Light),
    ];

    private const string AllMonitorsKey = "all";
    private const string PrimaryMonitorKey = "primary";

    protected override string PageTitle => "一般";

    private static string MonitorKey(WidgetMonitorMode mode, string? device) => mode switch
    {
        WidgetMonitorMode.Primary => PrimaryMonitorKey,
        WidgetMonitorMode.Specific when !string.IsNullOrEmpty(device) => device,
        _ => AllMonitorsKey,
    };

    protected override void Build()
    {
        var general = Current.General;

        AddSection("外観");
        AddCard("テーマ", "ダークは黒、ライトは白の背景で表示します。", "\uE790", Choice(s_themes, general.Theme, theme =>
        {
            general.Theme = theme;
            Save();
        }));

        AddSection("タスクバー");
        AddCard("表示のしかた", "ウィジェットは、タスクバーの通知領域（サブモニターでは時計）の左にアイコンと音量を出します。クリック・右クリック・ホイールは通知領域のアイコンと同じです。", "\uE7C4", Choice(App.TaskbarDisplayChoices, general.TrayDisplay, mode =>
        {
            general.TrayDisplay = mode;
            Save();
            Rebuild();
        }));

        // 選択肢の値は「all」「primary」かモニター名（DISPLAY1 など、GetMonitorInfo の名前）
        var monitorChoices = App.GetWidgetMonitorChoices(general)
            .Select(c => (c.Label, Key: MonitorKey(c.Mode, c.Device)))
            .ToList();
        var monitorCombo = Choice(monitorChoices, MonitorKey(general.WidgetMonitor, general.WidgetMonitorDevice), key =>
        {
            general.WidgetMonitor = key switch
            {
                AllMonitorsKey => WidgetMonitorMode.All,
                PrimaryMonitorKey => WidgetMonitorMode.Primary,
                _ => WidgetMonitorMode.Specific,
            };
            general.WidgetMonitorDevice = general.WidgetMonitor == WidgetMonitorMode.Specific ? key : null;
            Save();
        });
        monitorCombo.IsEnabled = general.TrayDisplay != TrayDisplayMode.TrayIcon;
        AddCard("ウィジェットの表示先", "ウィジェットを出すモニターを選びます。選んだモニターにタスクバーが無いときは、通知領域のアイコンを出します。", Glyphs.Monitor, monitorCombo);

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
        AddCard("ホイール 1 段で変える音量（%）", "トレイアイコン・ウィジェット・スライダーの上でホイールを回したとき、音量ホットキーのときに使います。", "\uE962", step);

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
