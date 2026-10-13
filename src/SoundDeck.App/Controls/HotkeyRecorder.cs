using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SoundDeck.Core.Hotkeys;
using Windows.System;
using Windows.UI.Core;

namespace SoundDeck.App.Controls;

/// <summary>押してからキーを押すと、その組み合わせをホットキーとして受け取るボタン。</summary>
public sealed partial class HotkeyRecorder : Button
{
    private bool _recording;

    public HotkeyRecorder()
    {
        MinWidth = 180;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Click += (_, _) => StartRecording();
        PreviewKeyDown += OnPreviewKeyDown;
        LostFocus += (_, _) => StopRecording();
        UpdateText();
    }

    public event EventHandler<HotkeyGesture>? GestureChanged;

    public HotkeyGesture Gesture { get; set; }

    public void SetGesture(HotkeyGesture gesture)
    {
        Gesture = gesture;
        UpdateText();
    }

    private void StartRecording()
    {
        if (_recording)
        {
            return;
        }

        _recording = true;
        // 登録済みのホットキーが反応してしまわないよう、記録中は一時的に外す
        App.Current.Hotkeys.Suspend();
        Content = "キーを押してください（Esc で取り消し）";
    }

    private void StopRecording()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        App.Current.Hotkeys.Resume();
        UpdateText();
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        e.Handled = true;
        var key = (int)e.Key;
        var modifiers = CurrentModifiers();

        if (e.Key == VirtualKey.Escape && modifiers == HotkeyModifiers.None)
        {
            StopRecording();
            return;
        }

        if ((e.Key is VirtualKey.Back or VirtualKey.Delete) && modifiers == HotkeyModifiers.None)
        {
            // 割り当てを消す
            Gesture = default;
            StopRecording();
            GestureChanged?.Invoke(this, Gesture);
            return;
        }

        if (HotkeyGesture.IsModifierKey(key))
        {
            Content = new HotkeyGesture(modifiers, 0).ToString().Replace("未設定", "") + " ...";
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsValid)
        {
            Content = "Ctrl・Alt・Shift・Win のどれかと組み合わせてください";
            return;
        }

        Gesture = gesture;
        StopRecording();
        GestureChanged?.Invoke(this, Gesture);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        var modifiers = HotkeyModifiers.None;
        if (Down(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Down(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Down(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private void UpdateText() => Content = Gesture.ToString();
}
