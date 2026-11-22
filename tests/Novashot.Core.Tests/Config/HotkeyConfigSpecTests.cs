using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// S-1 修复（review C1 防线）：热键规格比较——App 层用它跳过「没变就别重注册」，
/// 因为任何 store.Update（含关窗写窗口尺寸）都会触发 Changed。
/// 统一交互（2026-11-23 第二轮）：配置只剩一个截图热键。
/// </summary>
public class HotkeyConfigSpecTests
{
    [Fact]
    public void SameSpecsAs_TrueWhenCaptureMatches()
    {
        var a = new HotkeyConfig { Capture = "PrintScreen" };
        var b = new HotkeyConfig { Capture = "PrintScreen" };
        Assert.True(a.SameSpecsAs(b));
    }

    [Fact]
    public void SameSpecsAs_FalseOnDifference()
    {
        var a = new HotkeyConfig { Capture = "PrintScreen" };
        Assert.False(a.SameSpecsAs(new HotkeyConfig { Capture = "F9" }));
    }

    [Fact]
    public void SameSpecsAs_FalseOnNull()
    {
        var a = new HotkeyConfig();
        Assert.False(a.SameSpecsAs(null));
    }
}
