using Prtsc.Core.Common;
using Prtsc.Platform.Mac.Windowing;
using Xunit;

namespace Prtsc.Mac.Tests;

/// <summary>
/// Overlay 铺屏 frame 换算：DisplayInfo 物理像素 Bounds → NSScreen 点单位 frame。
/// Apply 的 setFrame/setLevel/setCollectionBehavior 已由 2026-11-01 独立探针在真机
/// NSWindow 上实证（frame (0,96,2560,1314) → (0,0,2560,1440) 且不回弹），此处锁纯换算。
/// </summary>
public class MacOverlayChromeTests
{
    [Fact]
    public void ComputeFrame_RetinaDividesByScale()
    {
        var display = new DisplayInfo(0, "5K", new PixelRect(0, 0, 5120, 2880),
            new PixelRect(0, 0, 5120, 2880), 2.0, IsPrimary: true);
        var f = MacOverlayChrome.ComputeFrame(display);
        Assert.Equal(0, f.Origin.X);
        Assert.Equal(0, f.Origin.Y);
        Assert.Equal(2560, f.Size.Width);
        Assert.Equal(1440, f.Size.Height);
    }

    [Fact]
    public void ComputeFrame_SecondDisplayKeepsGlobalPointOrigin()
    {
        // 右侧副屏：物理像素 x=5120（主屏 5120px@2 结束处），自身 scale 1
        var display = new DisplayInfo(1, "ext", new PixelRect(5120, 0, 2560, 1440),
            new PixelRect(5120, 0, 2560, 1440), 1.0, IsPrimary: false);
        var f = MacOverlayChrome.ComputeFrame(display);
        Assert.Equal(5120, f.Origin.X);
        Assert.Equal(2560, f.Size.Width);
    }
}
