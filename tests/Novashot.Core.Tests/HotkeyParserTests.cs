using Novashot.Core.Hotkeys;
using Xunit;

namespace Novashot.Core.Tests;

/// <summary>
/// HotkeyParser characterization（自 Platform.Windows.Hotkeys 上提至 Core 时的行为锁定）。
/// 语义对齐 WinShot internal/hotkeys/hotkeys.go 的 ParseHotkeyString。
/// </summary>
public class HotkeyParserTests
{
    [Fact]
    public void PrintScreen_Alone_ParsesToVk2C()
    {
        var ok = HotkeyParser.TryParse("PrintScreen", out var mods, out var vk);
        Assert.True(ok);
        Assert.Equal(0u, mods);
        Assert.Equal(0x2Cu, vk);
    }

    [Fact]
    public void CtrlShiftP_CombinesModifiers()
    {
        var ok = HotkeyParser.TryParse("Ctrl+Shift+P", out var mods, out var vk);
        Assert.True(ok);
        // MOD_CONTROL=0x2, MOD_SHIFT=0x4
        Assert.Equal(0x6u, mods);
        Assert.Equal(0x50u, vk);
    }

    [Fact]
    public void InvalidSpec_ReturnsFalse()
    {
        Assert.False(HotkeyParser.TryParse("NotAMod+NoKey", out _, out _));
        Assert.False(HotkeyParser.TryParse("", out _, out _));
        Assert.False(HotkeyParser.TryParse("   ", out _, out _));
    }

    [Fact]
    public void FunctionKeys_MapToVkRange()
    {
        Assert.True(HotkeyParser.TryParse("F12", out _, out var vk));
        Assert.Equal(0x7Bu, vk);
    }

    [Fact]
    public void CaseInsensitive_KeyAndModifier()
    {
        Assert.True(HotkeyParser.TryParse("cTrL+pRiNtScReEn", out var mods, out var vk));
        Assert.Equal(0x2u, mods);
        Assert.Equal(0x2Cu, vk);
    }

    [Fact]
    public void WinMetaAliases_MapToModWin()
    {
        Assert.True(HotkeyParser.TryParse("Meta+A", out var mods1, out _));
        Assert.True(HotkeyParser.TryParse("Win+A", out var mods2, out _));
        // MOD_WIN=0x8
        Assert.Equal(0x8u, mods1);
        Assert.Equal(mods2, mods1);
    }

    [Fact]
    public void Digits_MapToVk()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+1", out _, out var vk));
        Assert.Equal(0x31u, vk);
    }
}
