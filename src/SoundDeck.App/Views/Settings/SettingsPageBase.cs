using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.Controls;
using SoundDeck.App.Services;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Views.Settings;

/// <summary>
/// 設定ページの共通部分。ページの中身は Windows 11 の設定アプリと同じ「カードを縦に並べる」形で、コードで組み立てる。
/// </summary>
public abstract partial class SettingsPageBase : Page
{
    private readonly StackPanel _body = new() { Spacing = 4, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };

    protected SettingsPageBase()
    {
        Content = new ScrollViewer
        {
            Padding = new Thickness(36, 8, 36, 36),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _body,
        };
        Loaded += (_, _) =>
        {
            Audio.DevicesChanged += OnDevicesChanged;
            Rebuild();
        };
        Unloaded += (_, _) => Audio.DevicesChanged -= OnDevicesChanged;
    }

    internal static AudioService Audio => App.Current.Audio;

    internal static SettingsService SettingsService => App.Current.Settings;

    internal static AppSettings Current => App.Current.Settings.Current;

    /// <summary>デバイスの増減で中身を作り直すページは true にする。</summary>
    protected virtual bool RebuildOnDeviceChange => false;

    protected abstract string PageTitle { get; }

    /// <summary>ページの中身を作る。</summary>
    protected abstract void Build();

    protected void Rebuild()
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock
        {
            Text = PageTitle,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            Margin = new Thickness(0, 12, 0, 16),
        });
        Build();
    }

    protected void Save() => SettingsService.NotifyChanged();

    protected void Add(UIElement element) => _body.Children.Add(element);

    protected void AddSection(string text) => Add(new TextBlock
    {
        Text = text,
        Style = (Style)Application.Current.Resources["SettingsSectionHeaderStyle"],
    });

    protected void AddNote(string text) => Add(new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["SecondaryCaptionTextBlockStyle"],
        Margin = new Thickness(2, 0, 0, 8),
    });

    protected SettingsCard AddCard(string header, string description, string glyph, object? content)
    {
        var card = new SettingsCard { Header = header, Description = description, Glyph = glyph, Content = content };
        Add(card);
        return card;
    }

    protected static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "オン", OffContent = "オフ", MinWidth = 0 };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    protected static Button IconButton(string glyph, string tooltip, Action click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleIconButtonStyle"],
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }

    protected static Button TextButton(string text, Action click, bool accent = false)
    {
        var button = new Button { Content = text };
        if (accent)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        button.Click += (_, _) => click();
        return button;
    }

    protected static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    /// <summary>ComboBox に (表示名, 値) の組を並べる。</summary>
    protected static ComboBox Choice<T>(IEnumerable<(string Label, T Value)> items, T? selected, Action<T> changed, double minWidth = 220)
    {
        var combo = new ComboBox { MinWidth = minWidth };
        foreach (var (label, value) in items)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            if (EqualityComparer<T>.Default.Equals(value, selected!))
            {
                combo.SelectedIndex = combo.Items.Count - 1;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value })
            {
                changed(value);
            }
        };
        return combo;
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (RebuildOnDeviceChange)
        {
            Rebuild();
        }
    }
}
