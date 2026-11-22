using Novashot.Core.Common;
using Novashot.Core.Windows;
using Xunit;

namespace Novashot.Core.Tests.Windows;

/// <summary>
/// 窗口吸附命中：Z 序第一个 Bounds 包含指针者胜。PixelRect.Contains 为半开区间 [X, Right)。
/// </summary>
public class SnapHitTesterTests
{
    private static WindowInfo Win(int x, int y, int w, int h, string title = "w") =>
        new(Handle: 0, Title: title, ProcessName: "proc", Bounds: new PixelRect(x, y, w, h), IsMinimized: false);

    [Fact]
    public void Hit_ZOrderFirstContainingWindowWins()
    {
        var list = new[] { Win(0, 0, 500, 400, "front"), Win(0, 0, 1000, 800, "back") };

        var hit = SnapHitTester.Hit(list, 100, 100);

        Assert.NotNull(hit);
        Assert.Equal("front", hit.Title);
    }

    [Fact]
    public void Hit_OutsideAllBounds_ReturnsNull()
    {
        var list = new[] { Win(0, 0, 500, 400) };

        Assert.Null(SnapHitTester.Hit(list, 600, 100));
        Assert.Null(SnapHitTester.Hit(list, -1, 100));
    }

    [Fact]
    public void Hit_EmptyList_ReturnsNull()
    {
        Assert.Null(SnapHitTester.Hit(Array.Empty<WindowInfo>(), 10, 10));
    }

    [Fact]
    public void Hit_BoundsAreHalfOpen()
    {
        var list = new[] { Win(10, 10, 100, 100) };

        Assert.NotNull(SnapHitTester.Hit(list, 109, 109)); // Right/Bottom-1 在界内
        Assert.NotNull(SnapHitTester.Hit(list, 10, 10));   // 左上角闭
        Assert.Null(SnapHitTester.Hit(list, 110, 50));     // Right 排他
        Assert.Null(SnapHitTester.Hit(list, 50, 110));     // Bottom 排他
    }

    [Fact]
    public void Hit_VirtualDesktopNegativeOrigin_Works()
    {
        // 虚拟桌面原点可为负（副屏在左）
        var list = new[] { Win(-1920, 0, 1920, 1080, "left") };

        var hit = SnapHitTester.Hit(list, -100, 500);
        Assert.NotNull(hit);
        Assert.Equal("left", hit.Title);
    }
}
