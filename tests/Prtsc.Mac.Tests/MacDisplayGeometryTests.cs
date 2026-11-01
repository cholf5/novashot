using Prtsc.Platform.Mac.Capture;
using Prtsc.Platform.Mac.Interop;
using Xunit;

namespace Prtsc.Mac.Tests;

/// <summary>
/// MacDisplayGeometry 纯函数语义：CG 全局坐标（点，左上原点）× Scale → 物理像素 DisplayInfo。
/// 用例覆盖 Review Focus：Retina scale、副屏负坐标、left-to-right 排序。
/// </summary>
public class MacDisplayGeometryTests
{
    [Fact]
    public void Retina_MainDisplay_PhysicalBoundsArePointsTimesTwo()
    {
        // 13" MBP 内屏：1512x982 点 @2x
        var raw = new MacDisplayRaw(1, 1512, 982, new CGRect(0, 0, 1512, 982), IsMain: true);
        var info = MacDisplayGeometry.ToDisplayInfo(raw, scaleFactor: 2.0, index: 0);

        Assert.Equal(0, info.Index);
        Assert.Equal(3024, info.Bounds.Width);
        Assert.Equal(1964, info.Bounds.Height);
        Assert.Equal(2.0, info.ScaleFactor);
        Assert.True(info.IsPrimary);
        Assert.Equal(0, info.Bounds.X);
        Assert.Equal(0, info.Bounds.Y);
    }

    [Fact]
    public void NonRetina_Display_ScaleOne()
    {
        var raw = new MacDisplayRaw(2, 2560, 1440, new CGRect(0, 0, 2560, 1440), IsMain: true);
        var info = MacDisplayGeometry.ToDisplayInfo(raw, scaleFactor: 1.0, index: 0);
        Assert.Equal(2560, info.Bounds.Width);
        Assert.Equal(1.0, info.ScaleFactor);
    }

    [Fact]
    public void LeftPlacedSecondaryDisplay_HasNegativeX()
    {
        // 副屏在主屏左侧：2560 宽 → 全局 x = -2560
        var raw = new MacDisplayRaw(2, 2560, 1440, new CGRect(-2560, 0, 2560, 1440), IsMain: false);
        var info = MacDisplayGeometry.ToDisplayInfo(raw, scaleFactor: 1.0, index: 1);
        Assert.Equal(-2560, info.Bounds.X);
        Assert.False(info.IsPrimary);
    }

    [Fact]
    public void AbovePlacedSecondaryDisplay_HasNegativeY()
    {
        var raw = new MacDisplayRaw(2, 1920, 1080, new CGRect(0, -1080, 1920, 1080), IsMain: false);
        var info = MacDisplayGeometry.ToDisplayInfo(raw, scaleFactor: 1.0, index: 1);
        Assert.Equal(-1080, info.Bounds.Y);
    }

    [Fact]
    public void SortDisplays_OrdersLeftToRightThenTopToBottom()
    {
        var raws = new[]
        {
            new MacDisplayRaw(10, 1920, 1080, new CGRect(1920, 0, 1920, 1080), IsMain: false), // 主屏右侧
            new MacDisplayRaw(11, 1920, 1080, new CGRect(0, 0, 1920, 1080), IsMain: true),     // 主屏
            new MacDisplayRaw(12, 1920, 1080, new CGRect(0, -1080, 1920, 1080), IsMain: false), // 主屏上方
        };
        var sorted = MacDisplayGeometry.SortForIndexing(raws);

        Assert.Equal(12u, sorted[0].Id);    // 上方（y 更小）最先
        Assert.Equal(11u, sorted[1].Id);    // 主屏
        Assert.Equal(10u, sorted[2].Id);    // 右侧
    }

    [Fact]
    public void VirtualDesktopBounds_IsUnionOfAllDisplays()
    {
        var displays = new[]
        {
            new Prtsc.Core.Common.DisplayInfo(0, "d0", new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1304), 1.0, false),
            new Prtsc.Core.Common.DisplayInfo(1, "d1", new(0, -1080, 1920, 1080), new(0, -1080, 1920, 1080), 1.0, true),
        };
        var union = MacDisplayGeometry.VirtualDesktopBounds(displays);

        Assert.Equal(-2560, union.X);
        Assert.Equal(-1080, union.Y);
        Assert.Equal(2560 + 1920, union.Width);
        // 副屏 y=0..1440，主屏 y=-1080..0，纵向相邻无重叠 → 高度 1080+1440
        Assert.Equal(1080 + 1440, union.Height);
    }

    [Fact]
    public void IntersectingDisplays_ForRegionAcrossTwoScreens_FindsBoth()
    {
        var displays = new[]
        {
            new Prtsc.Core.Common.DisplayInfo(0, "d0", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080), 1.0, true),
            new Prtsc.Core.Common.DisplayInfo(1, "d1", new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1440), 1.0, false),
        };
        // 跨两屏的 region：x 1800..2000
        var region = new Prtsc.Core.Common.PixelRect(1800, 100, 200, 200);

        var hits = MacDisplayGeometry.IntersectingDisplays(displays, region);
        Assert.Equal(2, hits.Count);
        Assert.Equal(0, hits[0].Index);
        Assert.Equal(1, hits[1].Index);
    }

    [Fact]
    public void IntersectingDisplays_EmptyRegion_ReturnsNone()
    {
        var displays = new[]
        {
            new Prtsc.Core.Common.DisplayInfo(0, "d0", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080), 1.0, true),
        };
        var region = new Prtsc.Core.Common.PixelRect(5000, 5000, 100, 100);
        Assert.Empty(MacDisplayGeometry.IntersectingDisplays(displays, region));
    }
}
