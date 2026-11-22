using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class AngleSnapTests
{
    [Theory]
    [InlineData(100, 3, 100, 0)]      // 近水平 → 吸附 0°
    [InlineData(3, 100, 0, 100)]      // 近垂直 → 吸附 90°
    [InlineData(70, 68, 70, 70)]      // 近 45° 对角 → 最大分量
    [InlineData(70, -71, 71, -71)]    // 近 -45° 对角 → 最大分量
    [InlineData(50, 0, 50, 0)]        // 精确值不变
    public void Snap45_AppliesToNearestDiagonal(float dx, float dy, float ex, float ey)
    {
        var snapped = AngleSnap.Snap45(dx, dy);
        Assert.Equal(ex, snapped.X, 0.5f);
        Assert.Equal(ey, snapped.Y, 0.5f);
    }

    [Theory]
    [InlineData(100, 12, 100, 0)]     // 近水平（<10°）
    [InlineData(12, 100, 0, 100)]     // 近垂直
    [InlineData(45, 45, 45, 45)]      // 45° 不属于水平/垂直 → 原样
    [InlineData(60, 60, 60, 60)]      // 对角之外原样
    public void SnapOrtho_OnlyHorizontalVertical(float dx, float dy, float ex, float ey)
    {
        var snapped = AngleSnap.SnapOrtho(dx, dy);
        Assert.Equal(ex, snapped.X, 0.5f);
        Assert.Equal(ey, snapped.Y, 0.5f);
    }

    [Fact]
    public void Snap_DegenerateVector_ReturnsOriginal()
    {
        var v = AngleSnap.Snap45(0.5f, 0.5f);
        Assert.Equal(0.5f, v.X, 3f);
        Assert.Equal(0.5f, v.Y, 3f);
    }

    [Fact]
    public void Snap45_PreservesLength()
    {
        var snapped = AngleSnap.Snap45(100, 3);
        Assert.Equal(100, MathF.Sqrt(snapped.X * snapped.X + snapped.Y * snapped.Y), 1f);
    }
}
