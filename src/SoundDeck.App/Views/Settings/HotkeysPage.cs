using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SoundDeck.App.Controls;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Hotkeys;

namespace SoundDeck.App.Views.Settings;

public sealed partial class HotkeysPage : SettingsPageBase
{
    private static readonly (string Label, HotkeyAction Value)[] s_actions =
    [
        ("マイクのミュート切り替え", HotkeyAction.ToggleMicMute),
        ("フライアウトを開く・閉じる", HotkeyAction.ToggleFlyout),
        ("既定デバイスの音量を上げる", HotkeyAction.DefaultVolumeUp),
        ("既定デバイスの音量を下げる", HotkeyAction.DefaultVolumeDown),
        ("既定デバイスのミュート切り替え", HotkeyAction.ToggleDefaultMute),
        ("既定の出力デバイスを順に切り替え", HotkeyAction.CycleDefaultOutput),
        ("プロファイルを適用", HotkeyAction.ApplyProfile),
        ("設定を開く", HotkeyAction.OpenSettings),
    ];

    protected override string PageTitle => "ホットキー";

    protected override void Build()
    {
        AddNote("ほかのアプリを使っている最中でも効きます。入力欄を押してからキーを押すと割り当てられます（Backspace で解除）。");

        foreach (var binding in Current.Hotkeys)
        {
            Add(CreateRow(binding));
        }

        Add(new Button
        {
            Content = Row(new FontIcon { Glyph = Glyphs.Add, FontSize = 14 }, new TextBlock { Text = "ホットキーを追加" }),
            Margin = new Thickness(0, 8, 0, 0),
        }.Also(b => b.Click += (_, _) =>
        {
            Current.Hotkeys.Add(new HotkeyBinding { Action = HotkeyAction.ToggleMicMute });
            Save();
            Rebuild();
        }));
    }

    private UIElement CreateRow(HotkeyBinding binding)
    {
        var profileCombo = Choice(
            Current.Profiles.Select(p => (p.Name, p.Id)),
            binding.ProfileId ?? Guid.Empty,
            id =>
            {
                binding.ProfileId = id;
                Save();
            });
        profileCombo.PlaceholderText = "プロファイルを選択";
        profileCombo.Visibility = binding.Action == HotkeyAction.ApplyProfile ? Visibility.Visible : Visibility.Collapsed;

        var actionCombo = Choice(s_actions, binding.Action, action =>
        {
            binding.Action = action;
            profileCombo.Visibility = action == HotkeyAction.ApplyProfile ? Visibility.Visible : Visibility.Collapsed;
            Save();
        }, minWidth: 260);

        var status = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        UpdateStatus(binding, status);

        var recorder = new HotkeyRecorder();
        recorder.SetGesture(binding.Gesture);
        recorder.GestureChanged += (_, gesture) =>
        {
            binding.Modifiers = gesture.Modifiers;
            binding.VirtualKey = gesture.VirtualKey;
            Save();
            UpdateStatus(binding, status);
        };

        var enabled = Toggle(binding.Enabled, on =>
        {
            binding.Enabled = on;
            Save();
            UpdateStatus(binding, status);
        });

        var delete = IconButton(Glyphs.Delete, "削除", () =>
        {
            Current.Hotkeys.Remove(binding);
            Save();
            Rebuild();
        });

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(Row(actionCombo, profileCombo));
        left.Children.Add(status);
        grid.Children.Add(left);

        var right = Row(recorder, enabled, delete);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Padding = new Thickness(16, 12, 12, 12),
            Child = grid,
        };
    }

    private static void UpdateStatus(HotkeyBinding binding, TextBlock status)
    {
        // 保存のあとホットキーが登録し直されるので、結果を見るのは少し後
        status.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            status.Text = binding.Enabled && binding.Gesture.IsValid
                && App.Current.Hotkeys.Results.TryGetValue(binding.Id, out var ok) && !ok
                ? "このキーはほかのアプリが使っているため登録できませんでした"
                : "";
            status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
    }
}

internal static class FluentExtensions
{
    /// <summary>生成直後に設定を足すための小道具。</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
