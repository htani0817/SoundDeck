using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SoundDeck.App.Services;
using SoundDeck.App.ViewModels;
using SoundDeck.App.Win32;
using SoundDeck.Core.Commands;
using SoundDeck.Core.Profiles;
using Windows.Graphics;

namespace SoundDeck.App.Views;

/// <summary>トレイアイコンを押したときに出る音量パネル。</summary>
public sealed partial class FlyoutWindow : Window
{
    private const double WidthDip = 440;
    private const double MarginDip = 12;

    private readonly AudioService _audio;
    private readonly SettingsService _settings;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _peakTimer;
    private DateTime _lastHidden = DateTime.MinValue;
    private TaskbarEdge _edge = TaskbarEdge.Bottom;
    private Point _anchor;
    private bool _repositionPending;

    internal FlyoutWindow(AudioService audio, SettingsService settings)
    {
        _audio = audio;
        _settings = settings;
        InitializeComponent();

        _hwnd = WindowHelper.GetHwnd(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "SoundDeck";

        Activated += OnActivated;
        AppWindow.Closing += (_, e) =>
        {
            // 閉じずに隠す（Alt+F4 など）
            e.Cancel = true;
            Hide();
        };

        _peakTimer = DispatcherQueue.CreateTimer();
        _peakTimer.Interval = TimeSpan.FromMilliseconds(33);
        _peakTimer.Tick += (_, _) => _audio.UpdatePeaks();

        DeviceList.ItemsSource = _audio.OutputDevices;
        _audio.OutputDevices.CollectionChanged += (_, _) => OnDevicesChanged();
        _audio.InputDevices.CollectionChanged += (_, _) => OnDevicesChanged();
        UpdateEmptyText();

        // アプリの行が増減して一覧の高さが変わったら、ウィンドウの高さも合わせる
        // （スクロールの中身は表示領域いっぱいに広げられて縮んだことが分からないので、一覧の大きさで見る）
        DeviceList.SizeChanged += (_, _) => ScheduleReposition();
    }

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>トレイアイコンのクリックで開閉する。</summary>
    public void Toggle()
    {
        if (IsShown)
        {
            Hide();
            return;
        }

        // アイコンを押した瞬間にフライアウトのフォーカスが外れて閉じた直後なら、開き直さない
        if (DateTime.UtcNow - _lastHidden < TimeSpan.FromMilliseconds(300))
        {
            return;
        }

        Show();
    }

    public void Show()
    {
        NativeMethods.GetCursorPos(out _anchor);
        _edge = WindowHelper.GetTaskbarEdge();
        Reposition();

        Root.Opacity = 0;
        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        // 初回は表示するまで中身の高さが決まらないので、レイアウト後に合わせ直してから動かす
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Reposition();
            PlayShowAnimation();
        });

        if (_settings.Current.General.ShowPeakMeters)
        {
            _peakTimer.Start();
        }
    }

    /// <summary>
    /// 見た目の確認用。指定した点のあるモニターに、フォーカスを奪わずに表示する（全画面のゲーム中などでも邪魔をしない）。
    /// </summary>
    internal void ShowPreview(Point anchor, bool inputTab)
    {
        if (inputTab)
        {
            // XAML 側の初期選択より後に切り替える
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => TabBar.SelectedItem = InputTab);
        }

        _edge = TaskbarEdge.Bottom;
        _anchor = anchor;
        Reposition();
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Reposition);
        _peakTimer.Start();
    }

    public void Hide()
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }

        _peakTimer.Stop();
        _lastHidden = DateTime.UtcNow;
        AppWindow.Hide();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            return;
        }

        // メニューを開いたことでフォーカスが移った場合は閉じない。メニューが閉じた時点で、
        // フライアウトにフォーカスが戻っていなければ（外をクリックしていれば）閉じる
        var popups = Root.XamlRoot is { } root ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root) : [];
        if (popups.Count == 0)
        {
            Hide();
            return;
        }

        foreach (var popup in popups)
        {
            popup.Closed += OnPopupClosed;
        }
    }

    private void OnPopupClosed(object? sender, object e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.Popup popup)
        {
            popup.Closed -= OnPopupClosed;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var stillOpen = Root.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;
            if (!stillOpen && NativeMethods.GetForegroundWindow() != _hwnd)
            {
                Hide();
            }
        });
    }

    private void OnDevicesChanged()
    {
        UpdateEmptyText();
        ScheduleReposition();
    }

    private void ScheduleReposition()
    {
        if (!IsShown || _repositionPending)
        {
            return;
        }

        _repositionPending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _repositionPending = false;
            if (IsShown)
            {
                Reposition();
            }
        });
    }

    private void UpdateEmptyText()
    {
        var source = DeviceList.ItemsSource as System.Collections.ICollection;
        EmptyText.Visibility = source is { Count: 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>タスクバーの位置に合わせて、通知領域のそばに置く。高さは中身に合わせ、画面に収まらなければスクロールさせる。</summary>
    private void Reposition()
    {
        var (work, _, scale) = WindowHelper.GetMonitorAt(_anchor);
        var width = (int)Math.Round(WidthDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);

        // 枠のぶん中身の表示領域は外側より小さいので、中身の大きさで測ってから枠の高さを足す
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        Root.Measure(new Windows.Foundation.Size((width - frameWidth) / scale, double.PositiveInfinity));
        var contentHeight = (int)Math.Ceiling(Math.Max(Root.DesiredSize.Height, 160) * scale);
        var height = Math.Min(contentHeight + frameHeight, work.Height - (margin * 2));

        var x = _edge == TaskbarEdge.Left ? work.Left + margin : work.Right - width - margin;
        var y = _edge == TaskbarEdge.Top ? work.Top + margin : work.Bottom - height - margin;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void PlayShowAnimation()
    {
        var from = _edge switch
        {
            TaskbarEdge.Top => -24,
            TaskbarEdge.Left or TaskbarEdge.Right => 0,
            _ => 24,
        };

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var slide = new DoubleAnimation { From = from, To = 0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease };
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(140) };
        Storyboard.SetTarget(slide, SlideTransform);
        Storyboard.SetTargetProperty(slide, "Y");
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    // ---- 見出し・下の帯 ----

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        DeviceList.ItemsSource = sender.SelectedItem == InputTab ? _audio.InputDevices : _audio.OutputDevices;
        EmptyText.Text = sender.SelectedItem == InputTab ? "入力デバイスがありません" : "出力デバイスがありません";
        UpdateEmptyText();
        ScheduleReposition();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        App.Current.OpenSettings();
    }

    private void OnOpenMixerClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:apps-volume"));
    }

    private void OnOpenSoundSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));
    }

    private void OnProfileMenuOpening(object sender, object e)
    {
        ProfileMenu.Items.Clear();
        foreach (var profile in _settings.Current.Profiles)
        {
            var item = new MenuFlyoutItem { Text = profile.Name, Icon = new FontIcon { Glyph = Glyphs.Sliders } };
            item.Click += (_, _) => _audio.ApplyProfile(profile);
            ProfileMenu.Items.Add(item);
        }

        if (_settings.Current.Profiles.Count == 0)
        {
            ProfileMenu.Items.Add(new MenuFlyoutItem { Text = "保存したプロファイルはありません", IsEnabled = false });
        }

        ProfileMenu.Items.Add(new MenuFlyoutSeparator());
        var save = new MenuFlyoutItem { Text = "今の状態を保存...", Icon = new FontIcon { Glyph = Glyphs.Add } };
        save.Click += (_, _) =>
        {
            ProfileNameBox.Text = "";
            SaveProfileFlyout.ShowAt(ProfileButton);
            ProfileNameBox.Focus(FocusState.Programmatic);
        };
        ProfileMenu.Items.Add(save);

        var manage = new MenuFlyoutItem { Text = "プロファイルを管理...", Icon = new FontIcon { Glyph = Glyphs.Settings } };
        manage.Click += (_, _) =>
        {
            Hide();
            App.Current.OpenSettings("profiles");
        };
        ProfileMenu.Items.Add(manage);
    }

    private void OnProfileNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            OnSaveProfileClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0)
        {
            name = $"プロファイル {_settings.Current.Profiles.Count + 1}";
        }

        _settings.Current.Profiles.Add(ProfilePlanner.Capture(name, _audio.GetSnapshot()));
        _settings.NotifyChanged();
        SaveProfileFlyout.Hide();
    }

    // ---- デバイス ----

    private void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DeviceViewModel device)
        {
            _audio.SetDefaultDevice(device.Id);
        }
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DeviceViewModel device)
        {
            device.IsExpanded = !device.IsExpanded;
        }
    }

    private void OnDeviceMuteClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DeviceViewModel device)
        {
            device.IsMuted = !device.IsMuted;
        }
    }

    private void OnDeviceMoreClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not DeviceViewModel device)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };

        var setDefault = new MenuFlyoutItem { Text = "既定のデバイスにする", Icon = new FontIcon { Glyph = Glyphs.Check }, IsEnabled = !device.IsDefault };
        setDefault.Click += (_, _) => _audio.SetDefaultDevice(device.Id);
        menu.Items.Add(setDefault);

        var setComm = new MenuFlyoutItem { Text = "既定の通信デバイスにする", Icon = new FontIcon { Glyph = Glyphs.Headset }, IsEnabled = !device.IsCommunicationsDefault };
        setComm.Click += (_, _) => _audio.SetCommunicationsDevice(device.Id);
        menu.Items.Add(setComm);

        menu.Items.Add(new MenuFlyoutSeparator());

        var hide = new MenuFlyoutItem { Text = "このデバイスを一覧から隠す", Icon = new FontIcon { Glyph = "\uED1A" }, IsEnabled = !device.IsDefault };
        hide.Click += (_, _) =>
        {
            GetCustomization(device.Id).Hidden = true;
            _settings.NotifyChanged();
        };
        menu.Items.Add(hide);

        var rename = new MenuFlyoutItem { Text = "名前を変える・隠したデバイスを戻す...", Icon = new FontIcon { Glyph = Glyphs.Edit } };
        rename.Click += (_, _) =>
        {
            Hide();
            App.Current.OpenSettings("devices");
        };
        menu.Items.Add(rename);

        menu.ShowAt((FrameworkElement)sender);
    }

    // ---- アプリ ----

    private void OnAppMuteClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AppViewModel app)
        {
            app.IsMuted = !app.IsMuted;
        }
    }

    private void OnAppMoreClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppViewModel app)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var flowLabel = app.Device.Flow == DeviceFlow.Render ? "出力先" : "入力元";

        if (!app.IsSystemSounds)
        {
            var routing = new MenuFlyoutSubItem { Text = flowLabel, Icon = new FontIcon { Glyph = Glyphs.Switch }, IsEnabled = _audio.IsAppRoutingSupported };
            var current = _audio.GetAppDevice(app);
            var followDefault = new RadioMenuFlyoutItem { Text = "既定に従う", GroupName = "route", IsChecked = current is null };
            followDefault.Click += (_, _) => _audio.SetAppDevice(app, null);
            routing.Items.Add(followDefault);
            routing.Items.Add(new MenuFlyoutSeparator());
            foreach (var device in _audio.GetAllDevices(app.Device.Flow))
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = device.FullName,
                    GroupName = "route",
                    IsChecked = string.Equals(current, device.Id, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => _audio.SetAppDevice(app, device.Id);
                routing.Items.Add(item);
            }

            menu.Items.Add(routing);
        }

        if (app.Device.Flow == DeviceFlow.Render && !app.IsSystemSounds)
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            var memory = _audio.Memory;
            var remember = new ToggleMenuFlyoutItem
            {
                Text = "音量と出力先を覚えて戻す",
                IsChecked = !memory.IsExcluded(app.AppKey),
                IsEnabled = memory.Settings.Enabled,
            };
            remember.Click += (_, _) =>
            {
                memory.SetExcluded(app.AppKey, !remember.IsChecked);
                _settings.NotifyChanged();
            };
            menu.Items.Add(remember);

            var triggers = _settings.Current.Ducking.TriggerApps;
            var isTrigger = triggers.Contains(app.AppKey, StringComparer.OrdinalIgnoreCase);
            var duck = new ToggleMenuFlyoutItem { Text = "このアプリが鳴ったら他を下げる", IsChecked = isTrigger };
            duck.Click += (_, _) =>
            {
                triggers.RemoveAll(k => string.Equals(k, app.AppKey, StringComparison.OrdinalIgnoreCase));
                if (duck.IsChecked)
                {
                    triggers.Add(app.AppKey);
                    _settings.Current.Ducking.Enabled = true;
                }

                _settings.NotifyChanged();
            };
            menu.Items.Add(duck);
        }

        menu.ShowAt((FrameworkElement)sender);
    }

    private void OnSliderWheel(object sender, PointerRoutedEventArgs e)
    {
        // スライダーの上でホイールを回すと音量を変える
        var slider = (Slider)sender;
        var delta = e.GetCurrentPoint(slider).Properties.MouseWheelDelta;
        var step = _settings.Current.General.ScrollStepPercent;
        slider.Value = Math.Clamp(slider.Value + (Math.Sign(delta) * step), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private Core.Settings.DeviceCustomization GetCustomization(string id)
    {
        if (!_settings.Current.Devices.TryGetValue(id, out var custom))
        {
            custom = new Core.Settings.DeviceCustomization();
            _settings.Current.Devices[id] = custom;
        }

        return custom;
    }
}
