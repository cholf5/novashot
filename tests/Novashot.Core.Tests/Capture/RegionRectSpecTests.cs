using Novashot.Core.Capture;
using Novashot.Core.Common;
using Xunit;

namespace Novashot.Core.Tests;

/// <summary>
/// G-26 Capture Launcher：X/Y/W/H 文本解析 + 虚拟桌面钳制（纯函数，物理像素坐标）。
/// </summary>
public class RegionRectSpecTests
{
    // —— TryParse ——

    [Fact]
    public void TryParse_ValidInputs_ReturnsRect()
    {
        var ok = RegionRectSpec.TryParse("10", "-20", "300", "200", out var rect, out var error);

        Assert.True(ok);
        Assert.Equal(new PixelRect(10, -20, 300, 200), rect); // 负坐标合法（多屏虚拟桌面）
        Assert.Equal(RegionRectErrorKind.None, error);
    }

    [Theory]
    [InlineData("abc", "0", "100", "100")]
    [InlineData("0", "", "100", "100")]
    [InlineData("0", "0", "1.5", "100")]
    [InlineData("0", "0", "100", "  x ")]
    public void TryParse_NonIntegerFields_Fails(string x, string y, string w, string h)
    {
        var ok = RegionRectSpec.TryParse(x, y, w, h, out _, out var error);

        Assert.False(ok);
        Assert.Equal(RegionRectErrorKind.NotANumber, error);
    }

    [Theory]
    [InlineData("0", "0", "0", "100")]
    [InlineData("0", "0", "100", "0")]
    [InlineData("0", "0", "-50", "100")]
    [InlineData("0", "0", "100", "-1")]
    public void TryParse_NonPositiveSize_Fails(string x, string y, string w, string h)
    {
        var ok = RegionRectSpec.TryParse(x, y, w, h, out _, out var error);

        Assert.False(ok);
        Assert.Equal(RegionRectErrorKind.NonPositiveSize, error);
    }

    [Fact]
    public void TryParse_NullOrWhitespace_FailsAsNotANumber()
    {
        Assert.False(RegionRectSpec.TryParse(null, "0", "10", "10", out _, out var e1));
        Assert.Equal(RegionRectErrorKind.NotANumber, e1);
        Assert.False(RegionRectSpec.TryParse("0", "  ", "10", "10", out _, out var e2));
        Assert.Equal(RegionRectErrorKind.NotANumber, e2);
    }

    [Fact]
    public void TryParse_TrimsWhitespace_AroundNumbers()
    {
        var ok = RegionRectSpec.TryParse(" 12 ", " 0", "34 ", " 56", out var rect, out _);
        Assert.True(ok);
        Assert.Equal(new PixelRect(12, 0, 34, 56), rect);
    }

    // —— ClampToBounds ——

    [Fact]
    public void Clamp_InsideBounds_Unchanged()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var rect = new PixelRect(100, 100, 300, 200);
        Assert.Equal(rect, RegionRectSpec.ClampToBounds(rect, bounds));
    }

    [Fact]
    public void Clamp_NegativeCoordinates_ShiftsIntoBounds()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var rect = new PixelRect(-50, -50, 100, 100);
        Assert.Equal(new PixelRect(0, 0, 100, 100), RegionRectSpec.ClampToBounds(rect, bounds));
    }

    [Fact]
    public void Clamp_OverRightBottom_ShiftsBackWithoutResizing()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var rect = new PixelRect(1900, 1050, 100, 100);
        Assert.Equal(new PixelRect(1820, 980, 100, 100), RegionRectSpec.ClampToBounds(rect, bounds));
    }

    [Fact]
    public void Clamp_OversizedRect_ShrinksToBounds()
    {
        // 先截宽高到 bounds 上限，再位移 X/Y 入界：超尺寸区域钳成整屏
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var rect = new PixelRect(100, 100, 3000, 2000);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), RegionRectSpec.ClampToBounds(rect, bounds));
    }

    [Fact]
    public void Clamp_RespectsNegativeOriginBounds()
    {
        // 左侧副屏：虚拟桌面原点为负（Windows 常见布局）
        var bounds = new PixelRect(-1920, 0, 1920, 1080);
        var rect = new PixelRect(-2000, -10, 400, 300);
        Assert.Equal(new PixelRect(-1920, 0, 400, 300), RegionRectSpec.ClampToBounds(rect, bounds));
    }

    [Fact]
    public void Clamp_EmptyRect_ReturnsDefault()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        Assert.Equal(default(PixelRect), RegionRectSpec.ClampToBounds(default, bounds));
        Assert.Equal(default(PixelRect), RegionRectSpec.ClampToBounds(new PixelRect(10, 10, 0, 50), bounds));
    }

    [Fact]
    public void Clamp_EmptyBounds_ReturnsRectUnchanged()
    {
        // 无显示器（headless/异常路径）：不做钳制，让调用方自己的空判兜底
        var rect = new PixelRect(5, 5, 10, 10);
        Assert.Equal(rect, RegionRectSpec.ClampToBounds(rect, default));
    }

    // —— VirtualDesktopBounds ——

    [Fact]
    public void VirtualDesktopBounds_UnionsAllDisplays()
    {
        var displays = new List<DisplayInfo>
        {
            new(0, "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0, true),
            new(1, "\\\\.\\DISPLAY2", new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1.0, false),
        };

        Assert.Equal(new PixelRect(-1920, 0, 3840, 1080), RegionRectSpec.VirtualDesktopBounds(displays));
    }

    [Fact]
    public void VirtualDesktopBounds_EmptyList_ReturnsDefault()
    {
        Assert.Equal(default(PixelRect), RegionRectSpec.VirtualDesktopBounds(new List<DisplayInfo>()));
    }
}
