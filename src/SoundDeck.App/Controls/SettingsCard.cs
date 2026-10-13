using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SoundDeck.App.Controls;

/// <summary>設定画面の 1 項目（アイコン・見出し・説明・右側の操作部品）。</summary>
public sealed partial class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata("", OnDescriptionChanged));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public SettingsCard()
    {
        DefaultStyleKey = typeof(SettingsCard);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateDescriptionVisibility();
        if (GetTemplateChild("IconPart") is FrameworkElement icon)
        {
            icon.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingsCard)d).UpdateDescriptionVisibility();

    private void UpdateDescriptionVisibility()
    {
        if (GetTemplateChild("DescriptionPart") is FrameworkElement description)
        {
            description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
