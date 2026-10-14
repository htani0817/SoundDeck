using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.Views.Settings;
using SoundDeck.App.Win32;
using Windows.Graphics;

namespace SoundDeck.App.Views;

/// <summary>設定ウィンドウ（Windows 11 の設定アプリと同じ左ナビゲーション。背景はテーマに合わせた黒・白）。</summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, Type> s_pages = new()
    {
        ["general"] = typeof(GeneralPage),
        ["hotkeys"] = typeof(HotkeysPage),
        ["devices"] = typeof(DevicesPage),
        ["memory"] = typeof(MemoryPage),
        ["profiles"] = typeof(ProfilesPage),
        ["rules"] = typeof(RulesPage),
        ["ducking"] = typeof(DuckingPage),
        ["about"] = typeof(AboutPage),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SoundDeck.ico"));

        var scale = WindowHelper.GetScale(WindowHelper.GetHwnd(this));
        AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(760 * scale)));
    }

    /// <summary>見た目の確認用。指定した点のあるモニターに、フォーカスを奪わずに表示する。</summary>
    internal void ShowPreview(Point anchor)
    {
        var (work, _, _) = WindowHelper.GetMonitorAt(anchor);
        AppWindow.Move(new PointInt32(work.Left + 40, work.Top + 40));

        // アクティブにしないと他のウィンドウの裏に回るので、確認用のときだけ最前面にする
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        NativeMethods.ShowWindow(WindowHelper.GetHwnd(this), NativeMethods.SwShowNoActivate);
    }

    /// <summary>ページを開く。null なら最初のページ。</summary>
    public void Navigate(string? tag)
    {
        tag ??= (Nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "general";
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null)
        {
            Nav.SelectedItem = item;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && s_pages.TryGetValue(tag, out var page))
        {
            // XAML を持たないページは Frame.Navigate で開けない（型情報が無く止まる）ため、直接作って載せる
            ContentFrame.Content = (Page)Activator.CreateInstance(page)!;
        }
    }
}
