using Avalonia;
using Novashot.App.Controls;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// E-1 M9：放大镜 + 浮签的摆放纯几何（DIP）。
/// 网格以光标为中心（中心格 = 光标所指像素，不贴边搬移——搬移会让高亮格与采样点脱节）；
/// 浮签跟随光标避让屏幕边缘：默认网格正下方，底部放不下翻上方，左右钳进安全边距。
/// </summary>
public class MagnifierLayoutTests
{
    private static readonly Size GridSize = new(132, 132); // 11 格 × 12
    private static readonly Size LabelSize = new(120, 34);
    private static readonly Size Surface = new(800, 600);

    [Fact]
    public void CursorInMiddle_GridIsCenteredOnCursor_LabelBelowGrid()
    {
        var p = MagnifierLayout.ComputePlacement(
            new Point(400, 300), GridSize, LabelSize, Surface);

        Assert.Equal(400, p.Grid.Center.X, 0.5);
        Assert.Equal(300, p.Grid.Center.Y, 0.5);

        Assert.Equal(p.Grid.Bottom + MagnifierLayout.Gap, p.Label.Y, 0.5);
        Assert.Equal(p.Grid.Center.X, p.Label.Center.X, 0.5);
        Assert.True(p.Label.Bottom < Surface.Height);
    }

    [Fact]
    public void CursorNearBottom_LabelFlipsAboveGrid()
    {
        var p = MagnifierLayout.ComputePlacement(
            new Point(400, 590), GridSize, LabelSize, Surface);

        Assert.Equal(p.Grid.Y - MagnifierLayout.Gap - LabelSize.Height, p.Label.Y, 0.5);
        Assert.True(p.Label.Bottom <= p.Grid.Y, "翻转到上方后浮签不得压住网格");
    }

    [Fact]
    public void CursorNearRightEdge_LabelClampedInsideSafetyMargin()
    {
        var p = MagnifierLayout.ComputePlacement(
            new Point(790, 300), GridSize, LabelSize, Surface);

        Assert.True(p.Label.Right <= Surface.Width - MagnifierLayout.Margin + 0.5);
    }

    [Fact]
    public void CursorNearLeftEdge_LabelClampedInsideSafetyMargin()
    {
        var p = MagnifierLayout.ComputePlacement(
            new Point(2, 300), GridSize, LabelSize, Surface);

        Assert.True(p.Label.X >= MagnifierLayout.Margin - 0.5);
    }

    [Fact]
    public void CursorNearTop_EverythingStaysOnSurface()
    {
        var p = MagnifierLayout.ComputePlacement(
            new Point(400, 2), GridSize, LabelSize, Surface);

        Assert.True(p.Label.Y >= 0);
        Assert.True(p.Label.Bottom <= Surface.Height);
    }

    [Fact]
    public void TinySurface_DoesNotProduceInvalidRects()
    {
        // 防御路径：极小表面（测试窗口）不出 NaN / 负尺寸
        var p = MagnifierLayout.ComputePlacement(
            new Point(10, 10), GridSize, LabelSize, new Size(50, 40));

        Assert.False(double.IsNaN(p.Label.X));
        Assert.False(double.IsNaN(p.Label.Y));
        Assert.True(p.Label.Width > 0);
        Assert.True(p.Label.Height > 0);
        Assert.True(p.Label.X >= 0);
    }

    [Fact]
    public void LabelAlwaysFullyInsideSurfaceHorizontally()
    {
        // 扫一遍横向位置：无论光标在哪，浮签横向都不出安全边距
        for (double x = 0; x <= 800; x += 37)
        {
            var p = MagnifierLayout.ComputePlacement(
                new Point(x, 300), GridSize, LabelSize, Surface);
            Assert.True(p.Label.X >= MagnifierLayout.Margin - 0.5, $"x={x}");
            Assert.True(p.Label.Right <= Surface.Width - MagnifierLayout.Margin + 0.5, $"x={x}");
        }
    }
}
