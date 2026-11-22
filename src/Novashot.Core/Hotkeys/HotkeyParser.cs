namespace Novashot.Core.Hotkeys;

/// <summary>
/// 把 "Ctrl+Shift+PrintScreen" 之类的字符串解析成 modifier flags + virtual-key。
/// 语义对齐 WinShot internal/hotkeys/hotkeys.go 的 ParseHotkeyString。
/// </summary>
public static class HotkeyParser
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public static bool TryParse(string spec, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "win":
                case "meta":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        var key = parts[^1];
        vk = VkFromName(key);
        return vk != 0;
    }

    private static uint VkFromName(string name)
    {
        var k = name.ToLowerInvariant();
        return k switch
        {
            "printscreen" or "novashot" or "novashotn" or "snapshot" => 0x2C,
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            _ when k.Length == 1 && k[0] >= 'a' && k[0] <= 'z' => (uint)(k[0] - 'a' + 0x41),
            _ when k.Length == 1 && k[0] >= '0' && k[0] <= '9' => (uint)(k[0] - '0' + 0x30),
            _ when k.StartsWith('f') && uint.TryParse(k.AsSpan(1), out var n) && n >= 1 && n <= 24 => 0x70 + n - 1,
            _ => 0,
        };
    }
}
