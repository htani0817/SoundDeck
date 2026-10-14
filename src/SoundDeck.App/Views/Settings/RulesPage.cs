using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Commands;
using SoundDeck.Core.Rules;

namespace SoundDeck.App.Views.Settings;

public sealed partial class RulesPage : SettingsPageBase
{
    private static readonly (string Label, RuleTriggerKind Value)[] s_triggers =
    [
        ("デバイスがつながったら", RuleTriggerKind.DeviceConnected),
        ("デバイスが外れたら", RuleTriggerKind.DeviceDisconnected),
        ("アプリが音を出し始めたら", RuleTriggerKind.AppAudioStarted),
    ];

    private static readonly (string Label, RuleActionKind Value)[] s_actions =
    [
        ("既定の出力デバイスを切り替える", RuleActionKind.SetDefaultOutput),
        ("既定の入力デバイスを切り替える", RuleActionKind.SetDefaultInput),
        ("プロファイルを適用する", RuleActionKind.ApplyProfile),
    ];

    private readonly AutomationRule _draft = new() { Trigger = RuleTriggerKind.DeviceConnected, Action = RuleActionKind.SetDefaultOutput };

    protected override string PageTitle => "自動切替ルール";

    protected override bool RebuildOnDeviceChange => true;

    protected override void Build()
    {
        AddNote("「ヘッドホンをつないだら既定をヘッドホンにする」「Discord が鳴り始めたら通話用プロファイルにする」のような自動切替を作れます。");

        AddSection("新しいルール");
        Add(CreateEditor());

        AddSection($"ルール（{Current.Rules.Count}）");
        if (Current.Rules.Count == 0)
        {
            AddNote("まだありません。");
        }

        foreach (var rule in Current.Rules.ToList())
        {
            var card = AddCard(rule.Name, Describe(rule), Glyphs.Lightning, Row(
                Toggle(rule.Enabled, on =>
                {
                    rule.Enabled = on;
                    Save();
                }),
                IconButton(Glyphs.Delete, "削除", () =>
                {
                    Current.Rules.Remove(rule);
                    Save();
                    Rebuild();
                })));
            _ = card;
        }
    }

    private Border CreateEditor()
    {
        var panel = new StackPanel { Spacing = 12 };

        var triggerTarget = new ComboBox { MinWidth = 300, PlaceholderText = "対象" };
        var actionTarget = new ComboBox { MinWidth = 300, PlaceholderText = "切り替え先" };

        void FillTriggerTargets()
        {
            triggerTarget.Items.Clear();
            if (_draft.Trigger == RuleTriggerKind.AppAudioStarted)
            {
                foreach (var app in KnownApps())
                {
                    triggerTarget.Items.Add(new ComboBoxItem { Content = app.Name, Tag = app.Key });
                }

                triggerTarget.PlaceholderText = "アプリ（いま動いているもの・音量を覚えているもの）";
            }
            else
            {
                foreach (var device in Audio.GetDeviceSummaries(DeviceFlow.Render).Concat(Audio.GetDeviceSummaries(DeviceFlow.Capture)))
                {
                    var label = $"{(device.Flow == DeviceFlow.Render ? "出力" : "入力")}: {device.Name}{(device.IsActive ? "" : "（未接続）")}";
                    triggerTarget.Items.Add(new ComboBoxItem { Content = label, Tag = device.Id });
                }

                triggerTarget.PlaceholderText = "デバイス（未接続のものも選べます）";
            }
        }

        void FillActionTargets()
        {
            actionTarget.Items.Clear();
            if (_draft.Action == RuleActionKind.ApplyProfile)
            {
                foreach (var profile in Current.Profiles)
                {
                    actionTarget.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id.ToString() });
                }

                actionTarget.PlaceholderText = Current.Profiles.Count == 0 ? "先にプロファイルを保存してください" : "プロファイル";
            }
            else
            {
                var flow = _draft.Action == RuleActionKind.SetDefaultOutput ? DeviceFlow.Render : DeviceFlow.Capture;
                foreach (var device in Audio.GetDeviceSummaries(flow))
                {
                    actionTarget.Items.Add(new ComboBoxItem { Content = device.Name + (device.IsActive ? "" : "（未接続）"), Tag = device.Id });
                }

                actionTarget.PlaceholderText = "切り替え先のデバイス";
            }
        }

        var triggerKind = Choice(s_triggers, _draft.Trigger, kind =>
        {
            _draft.Trigger = kind;
            FillTriggerTargets();
        });
        var actionKind = Choice(s_actions, _draft.Action, kind =>
        {
            _draft.Action = kind;
            FillActionTargets();
        });
        FillTriggerTargets();
        FillActionTargets();

        var nameBox = new TextBox { PlaceholderText = "ルールの名前（省略可）", Width = 300 };
        var error = new TextBlock
        {
            Style = (Style)Application.Current.Resources["ErrorCaptionTextBlockStyle"],
        };

        var add = TextButton("ルールを追加", () =>
        {
            if (triggerTarget.SelectedItem is not ComboBoxItem { Tag: string trigger } || actionTarget.SelectedItem is not ComboBoxItem { Tag: string target })
            {
                error.Text = "対象と切り替え先を選んでください";
                return;
            }

            var rule = new AutomationRule
            {
                Trigger = _draft.Trigger,
                Action = _draft.Action,
                TriggerDeviceId = _draft.Trigger == RuleTriggerKind.AppAudioStarted ? null : trigger,
                TriggerAppKey = _draft.Trigger == RuleTriggerKind.AppAudioStarted ? trigger : null,
                ActionDeviceId = _draft.Action == RuleActionKind.ApplyProfile ? null : target,
                ActionProfileId = _draft.Action == RuleActionKind.ApplyProfile ? Guid.Parse(target) : null,
            };
            rule.Name = nameBox.Text.Trim() is { Length: > 0 } name ? name : $"{(triggerTarget.SelectedItem as ComboBoxItem)?.Content} → {(actionTarget.SelectedItem as ComboBoxItem)?.Content}";
            Current.Rules.Add(rule);
            Save();
            Rebuild();
        }, accent: true);

        panel.Children.Add(Labeled("いつ", Row(triggerKind, triggerTarget)));
        panel.Children.Add(Labeled("何をする", Row(actionKind, actionTarget)));
        panel.Children.Add(Labeled("名前", Row(nameBox, add, error)));

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Padding = new Thickness(16),
            Child = panel,
        };
    }

    private static Grid Labeled(string label, UIElement content)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn((FrameworkElement)content, 1);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>今動いているアプリと、音量を覚えているアプリ。</summary>
    private static IEnumerable<(string Key, string Name)> KnownApps() =>
        Audio.GetRunningApps().Select(a => (a.AppKey, a.DisplayName))
            .Concat(Current.VolumeMemory.Apps.Select(p => (p.Key, p.Value.DisplayName)))
            .GroupBy(a => a.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Item2, StringComparer.CurrentCulture);

    private static string Describe(AutomationRule rule)
    {
        string Device(string? id) =>
            Audio.GetDeviceSummaries(DeviceFlow.Render).Concat(Audio.GetDeviceSummaries(DeviceFlow.Capture))
                .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? "（見つからないデバイス）";

        var when = rule.Trigger switch
        {
            RuleTriggerKind.DeviceConnected => $"「{Device(rule.TriggerDeviceId)}」がつながったら",
            RuleTriggerKind.DeviceDisconnected => $"「{Device(rule.TriggerDeviceId)}」が外れたら",
            RuleTriggerKind.AppAudioStarted => $"「{AppName(rule.TriggerAppKey)}」が音を出し始めたら",
            _ => "",
        };
        var what = rule.Action switch
        {
            RuleActionKind.SetDefaultOutput => $"既定の出力を「{Device(rule.ActionDeviceId)}」にする",
            RuleActionKind.SetDefaultInput => $"既定の入力を「{Device(rule.ActionDeviceId)}」にする",
            RuleActionKind.ApplyProfile => $"プロファイル「{Current.Profiles.FirstOrDefault(p => p.Id == rule.ActionProfileId)?.Name ?? "（削除済み）"}」を適用する",
            _ => "",
        };
        return $"{when} → {what}";
    }

    private static string AppName(string? key) =>
        KnownApps().FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)).Name
        ?? Path.GetFileName(key ?? "");
}
