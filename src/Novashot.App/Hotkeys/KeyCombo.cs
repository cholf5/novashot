using Avalonia.Input;

namespace Novashot.App.Hotkeys;

/// <summary>
/// S-1 热键录制：把 Avalonia 的 Key/KeyModifiers 转成
/// <see cref="Novashot.Core.Hotkeys.HotkeyParser"/> 词表内的 spec 串（如 "Ctrl+Shift+PrintScreen"）。
/// 修饰符前缀固定序 Ctrl+Alt+Shift+Win；Esc（取消录制的手势）、纯修饰键、词表外键（NumPad 等，
/// 对齐 WinShot 仅顶排数字）都返回 false。
/// </summary>
public static class KeyCombo
{
    public static bool TryToSpec(Key key, KeyModifiers mods, out string spec)
    {
        spec = string.Empty;

        var keyName = KeyName(key);
        if (keyName is null) return false;

        var parts = new List<string>(5);
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(keyName);

        spec = string.Join("+", parts);
        return true;
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.PrintScreen => "PrintScreen",
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Left => "Left",
        Key.Up => "Up",
        Key.Right => "Right",
        Key.Down => "Down",
        _ => null,
    };
}
