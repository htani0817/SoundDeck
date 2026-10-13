using Microsoft.UI.Xaml;
using SoundDeck.Audio;
using SoundDeck.Core.Commands;

namespace SoundDeck.App.ViewModels;

/// <summary>Segoe Fluent Icons の文字コード。</summary>
internal static class Glyphs
{
    public const string Volume = "\uE767";
    public const string Mute = "\uE74F";
    public const string Volume0 = "\uE992";
    public const string Volume1 = "\uE993";
    public const string Volume2 = "\uE994";
    public const string Volume3 = "\uE995";
    public const string Speakers = "\uE7F5";
    public const string Headphones = "\uE7F6";
    public const string Headset = "\uE95B";
    public const string Microphone = "\uE720";
    public const string MicrophoneOff = "\uF781";
    public const string Monitor = "\uE7F4";
    public const string Settings = "\uE713";
    public const string More = "\uE712";
    public const string Switch = "\uE8AB";
    public const string Check = "\uE73E";
    public const string Info = "\uE946";
    public const string History = "\uE81C";
    public const string Delete = "\uE74D";
    public const string Add = "\uE710";
    public const string Edit = "\uE70F";
    public const string Keyboard = "\uE765";
    public const string Apps = "\uE71D";
    public const string Sliders = "\uE9E9";
    public const string Lightning = "\uE945";
    public const string OpenExternal = "\uE8A7";
    public const string App = "\uE74C";

    /// <summary>音量に合わせたスピーカーの絵（Windows 11 のトレイと同じ段階）。</summary>
    public static string ForVolume(double volume0To100, bool muted) => muted
        ? Mute
        : volume0To100 switch
        {
            <= 0 => Volume0,
            < 34 => Volume1,
            < 67 => Volume2,
            _ => Volume3,
        };

    public static string ForMicrophone(bool muted) => muted ? MicrophoneOff : Microphone;

    public static string ForDevice(DeviceFormFactor formFactor, DeviceFlow flow) => flow == DeviceFlow.Capture
        ? Microphone
        : formFactor switch
        {
            DeviceFormFactor.Headphones => Headphones,
            DeviceFormFactor.Headset or DeviceFormFactor.Handset => Headset,
            DeviceFormFactor.DigitalAudioDisplayDevice => Monitor,
            _ => Speakers,
        };
}

/// <summary>x:Bind の関数バインドで使う小さな変換。</summary>
internal static class Bind
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfNull(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;

    public static string Percent(double value) => ((int)Math.Round(value)).ToString();

    public static double Opacity(bool dimmed) => dimmed ? 0.45 : 1.0;

    public static double InactiveOpacity(bool active) => active ? 1.0 : 0.7;
}
