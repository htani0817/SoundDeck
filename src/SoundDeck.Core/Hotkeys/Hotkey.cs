using System.Text.Json.Serialization;

namespace SoundDeck.Core.Hotkeys;

/// <summary>修飾キー。値は Win32 の MOD_ALT などと同じにしている。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public enum HotkeyAction
{
    ToggleFlyout,
    ToggleMicMute,
    DefaultVolumeUp,
    DefaultVolumeDown,
    ToggleDefaultMute,
    CycleDefaultOutput,
    ApplyProfile,
    OpenSettings,
}

public sealed class HotkeyBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public HotkeyAction Action { get; set; }

    /// <summary><see cref="HotkeyAction.ApplyProfile"/> のときに使うプロファイル。</summary>
    public Guid? ProfileId { get; set; }

    public HotkeyModifiers Modifiers { get; set; }

    /// <summary>Win32 の仮想キーコード。0 は未割り当て。</summary>
    public int VirtualKey { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public HotkeyGesture Gesture => new(Modifiers, VirtualKey);

    public static List<HotkeyBinding> CreateDefaults() =>
    [
        new() { Action = HotkeyAction.ToggleMicMute, Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey = 'M' },
        new() { Action = HotkeyAction.ToggleFlyout, Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey = 'V' },
    ];
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const int VkF1 = 0x70;
    private const int VkF24 = 0x87;

    /// <summary>修飾キーなしで登録してよいのは F キーだけ（普段の入力を奪わないため）。</summary>
    public bool IsValid =>
        VirtualKey != 0
        && !IsModifierKey(VirtualKey)
        && (Modifiers != HotkeyModifiers.None || VirtualKey is >= VkF1 and <= VkF24);

    public static bool IsModifierKey(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public override string ToString()
    {
        if (VirtualKey == 0)
        {
            return "未設定";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        >= VkF1 and <= VkF24 => "F" + (vk - VkF1 + 1),
        >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x6A => "Num *",
        0x6B => "Num +",
        0x6D => "Num -",
        0x6E => "Num .",
        0x6F => "Num /",
        0xAD => "VolumeMute",
        0xAE => "VolumeDown",
        0xAF => "VolumeUp",
        0xB0 => "NextTrack",
        0xB1 => "PrevTrack",
        0xB3 => "PlayPause",
        0xBA => ":",
        0xBB => ";",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "@",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "^",
        0xE2 => "\\ (ろ)",
        _ => $"0x{vk:X2}",
    };
}
