using Avalonia.Input;
using Novashot.App.Hotkeys;
using Novashot.Core.Hotkeys;

namespace Novashot.App.Tests;

/// <summary>
/// S-1 热键录制：Avalonia Key/KeyModifiers → HotkeyParser 词表 spec 串。
/// 修饰符前缀固定序 Ctrl+Alt+Shift+Win；产物必须能被 HotkeyParser 解析（词表对齐契约）。
/// </summary>
public class KeyComboTests
{
    [Theory]
    [InlineData(Key.V, KeyModifiers.Control, "Ctrl+V")]
    [InlineData(Key.PrintScreen, KeyModifiers.None, "PrintScreen")]
    [InlineData(Key.D1, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+1")]
    [InlineData(Key.F5, KeyModifiers.None, "F5")]
    [InlineData(Key.Delete, KeyModifiers.None, "Delete")]
    [InlineData(Key.Space, KeyModifiers.Alt, "Alt+Space")]
    [InlineData(Key.PageUp, KeyModifiers.Meta, "Win+PageUp")]
    public void TryToSpec_MapsParserVocabulary(Key key, KeyModifiers mods, string expected)
    {
        Assert.True(KeyCombo.TryToSpec(key, mods, out var spec));
        Assert.Equal(expected, spec);
        Assert.True(HotkeyParser.TryParse(spec, out _, out _), $"产物必须可解析: {spec}");
    }

    [Theory]
    [InlineData(Key.Escape, KeyModifiers.None)] // Esc 是取消录制的手势，不是热键
    [InlineData(Key.LeftCtrl, KeyModifiers.Control)] // 纯修饰键
    [InlineData(Key.RWin, KeyModifiers.None)]
    [InlineData(Key.None, KeyModifiers.None)]
    [InlineData(Key.NumPad0, KeyModifiers.None)] // 词表外（对齐 WinShot：仅顶排数字）
    public void TryToSpec_RejectsNonHotkeyKeys(Key key, KeyModifiers mods)
    {
        Assert.False(KeyCombo.TryToSpec(key, mods, out var spec));
        Assert.Equal(string.Empty, spec);
    }
}
