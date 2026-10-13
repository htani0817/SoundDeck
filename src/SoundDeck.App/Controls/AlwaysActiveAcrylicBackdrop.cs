using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SoundDeck.App.Controls;

/// <summary>
/// フォーカスが無くても透けたままのアクリル背景。
/// 標準の DesktopAcrylicBackdrop は非アクティブになると単色に戻るため、OSD やメニュー表示中のフライアウトで使う。
/// </summary>
public sealed partial class AlwaysActiveAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private FrameworkElement? _root;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null)
        {
            _root.ActualThemeChanged += OnThemeChanged;
        }

        UpdateTheme();
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_root is not null)
        {
            _root.ActualThemeChanged -= OnThemeChanged;
        }

        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => UpdateTheme();

    private void UpdateTheme()
    {
        if (_configuration is null || _root is null)
        {
            return;
        }

        _configuration.Theme = _root.ActualTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
    }
}
