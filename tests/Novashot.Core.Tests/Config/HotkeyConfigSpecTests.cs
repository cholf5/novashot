using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// S-1 修复（review C1 防线）：热键规格比较——App 层用它跳过「没变就别重注册」，
/// 因为任何 store.Update（含关窗写窗口尺寸）都会触发 Changed。
/// </summary>
public class HotkeyConfigSpecTests
{
    [Fact]
    public void SameSpecsAs_TrueWhenAllThreeMatch()
    {
        var a = new HotkeyConfig { Fullscreen = "PrintScreen", Region = "Ctrl+PrintScreen", Window = "Ctrl+Shift+PrintScreen" };
        var b = new HotkeyConfig { Fullscreen = "PrintScreen", Region = "Ctrl+PrintScreen", Window = "Ctrl+Shift+PrintScreen" };
        Assert.True(a.SameSpecsAs(b));
    }

    [Fact]
    public void SameSpecsAs_FalseOnAnyDifference()
    {
        var a = new HotkeyConfig { Fullscreen = "PrintScreen", Region = "Ctrl+PrintScreen", Window = "Ctrl+Shift+PrintScreen" };
        Assert.False(a.SameSpecsAs(new HotkeyConfig { Fullscreen = "F9", Region = "Ctrl+PrintScreen", Window = "Ctrl+Shift+PrintScreen" }));
        Assert.False(a.SameSpecsAs(new HotkeyConfig { Fullscreen = "PrintScreen", Region = "F10", Window = "Ctrl+Shift+PrintScreen" }));
        Assert.False(a.SameSpecsAs(new HotkeyConfig { Fullscreen = "PrintScreen", Region = "Ctrl+PrintScreen", Window = "F11" }));
    }

    [Fact]
    public void SameSpecsAs_FalseOnNull()
    {
        var a = new HotkeyConfig();
        Assert.False(a.SameSpecsAs(null));
    }
}
