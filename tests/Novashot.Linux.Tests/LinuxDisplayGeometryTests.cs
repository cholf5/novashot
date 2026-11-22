using Novashot.Core.Common;
using Novashot.Platform.Linux.Capture;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Linux.Tests;

/// <summary>XRandR 几何换算纯函数：DPI 推断、排序、虚拟桌面包围盒（含负坐标）、交集。</summary>
public class LinuxDisplayGeometryTests
{
    private static XDisplayRaw Disp(int x, int y, int w, int h, int mmW, bool primary = false, string name = "eDP-1") =>
        new((nint)0x10, name, x, y, w, h, mmW, (int)(h * 25.4 / 96.0), primary);

    [Theory]
    [InlineData(1920, 508, 1.0)]   // 1920px / 20in ≈ 96dpi
    [InlineData(2560, 516, 1.25)]  // ≈126 → 距 1.25(120) 6.4 在容差内
    [InlineData(3840, 560, 1.75)]  // ≈174.6 → 距 1.75(168) 6.6 在容差内
    [InlineData(1000, 500, null)]  // 50.8dpi 非标准 → null
    public void ScaleFromMillimeters_SnapsToStandardRatios(int px, int mm, double? expected)
    {
        var raw = Disp(0, 0, px, 1080, mm);
        Assert.Equal(expected, LinuxDisplayGeometry.ScaleFromMillimeters(raw));
    }

    [Fact]
    public void ScaleFromMillimeters_UnknownMm_ReturnsNull()
    {
        var raw = new XDisplayRaw(0x10, "DP-1", 0, 0, 1920, 1080, 0, 0, false);
        Assert.Null(LinuxDisplayGeometry.ScaleFromMillimeters(raw));
    }

    [Fact]
    public void SortForIndexing_PrimaryFirst()
    {
        var sorted = LinuxDisplayGeometry.SortForIndexing(
        [
            Disp(1920, 0, 1920, 1080, 508, name: "HDMI-0"),
            Disp(0, 0, 1920, 1080, 508, primary: true, name: "eDP-1"),
        ]);
        Assert.Equal("eDP-1", sorted[0].OutputName);
        Assert.Equal("HDMI-0", sorted[1].OutputName);
    }

    [Fact]
    public void VirtualDesktopBounds_SupportsNegativeCoords()
    {
        var a = LinuxDisplayGeometry.ToDisplayInfo(Disp(-1920, -200, 1920, 1080, 508), 1.0, 0);
        var b = LinuxDisplayGeometry.ToDisplayInfo(Disp(0, 0, 3840, 2160, 1016, primary: true), 1.0, 1);
        var bounds = LinuxDisplayGeometry.VirtualDesktopBounds([a, b]);

        Assert.Equal(-1920, bounds.X);
        Assert.Equal(-200, bounds.Y);
        Assert.Equal(5760, bounds.Width);   // -1920 → 3840
        Assert.Equal(2360, bounds.Height);  // -200 → 2160
    }

    [Fact]
    public void IntersectingDisplays_FiltersNonOverlapping()
    {
        var d1 = LinuxDisplayGeometry.ToDisplayInfo(Disp(0, 0, 100, 100, 25), 1.0, 0);
        var d2 = LinuxDisplayGeometry.ToDisplayInfo(Disp(100, 0, 100, 100, 25), 1.0, 1);
        var d3 = LinuxDisplayGeometry.ToDisplayInfo(Disp(1000, 0, 100, 100, 25), 1.0, 2);

        var hits = LinuxDisplayGeometry.IntersectingDisplays([d1, d2, d3], new PixelRect(50, 0, 100, 50));
        Assert.Equal(2, hits.Count);
        Assert.Equal(0, hits[0].Index);
        Assert.Equal(1, hits[1].Index);
    }

    [Fact]
    public void PrimaryScale_FallsBackToFirstDisplay()
    {
        var d1 = LinuxDisplayGeometry.ToDisplayInfo(Disp(0, 0, 100, 100, 25), 1.25, 0);
        var d2 = LinuxDisplayGeometry.ToDisplayInfo(Disp(100, 0, 100, 100, 25), 1.0, 1);
        Assert.Equal(1.25, LinuxDisplayGeometry.PrimaryScale([d1, d2])); // 都非 primary → 第一个的 scale
    }
}
