using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// Crop 几何纯函数：比例吸附（沿最长维内缩）、图像闭包钳制、标准化。
/// </summary>
public class CropRectsTests
{
    private static readonly SKRect Image = new(0, 0, 200, 100);

    // ---------- SnapToRatio ----------

    [Fact]
    public void SnapToRatio_AlreadyMatching_ReturnsEquivalent()
    {
        // 2:1 矩形 + 2:1 比例
        var rect = new SKRect(10, 10, 110, 60); // 100x50
        var snapped = CropRects.SnapToRatio(rect, 2, 1, Image);
        Assert.Equal(rect, snapped);
    }

    [Fact]
    public void SnapToRatio_AnchorsTopLeft_ShrinksLongerSide()
    {
        // 起点 100x50 (2:1)，吸 1:1 → 宽缩到 50，TopLeft 不动
        var rect = new SKRect(10, 10, 110, 60);
        var snapped = CropRects.SnapToRatio(rect, 1, 1, Image);
        Assert.Equal(10, snapped.Left);
        Assert.Equal(10, snapped.Top);
        Assert.Equal(50, snapped.Width);
        Assert.Equal(50, snapped.Height);
    }

    [Fact]
    public void SnapToRatio_TallRatio_ShrinksHeight()
    {
        // 100x50 吸 9:16 → 高 = 100*16/9 > 50，所以宽 = 50*9/16 = 28.125
        var rect = new SKRect(10, 10, 110, 60);
        var snapped = CropRects.SnapToRatio(rect, 9, 16, Image);
        Assert.Equal(10, snapped.Left);
        Assert.Equal(10, snapped.Top);
        Assert.Equal(28.125f, snapped.Width, 3f);
        Assert.Equal(50f, snapped.Height, 3f);
    }

    [Fact]
    public void SnapToRatio_ExtremeNearImageEdge_ClampsIntoImage()
    {
        // 矩形贴右下边缘：TopLeft 锚定吸 1:1 后越界 → 内缩再钳制
        var rect = new SKRect(150, 60, 200, 100); // 50x40 → 1:1 需 40x40 @ (150,60) → 右下 190,100 OK
        var snapped = CropRects.SnapToRatio(rect, 1, 1, Image);
        Assert.Equal(40f, snapped.Width, 2f);
        Assert.True(snapped.Right <= Image.Right + 0.01f);
        Assert.True(snapped.Bottom <= Image.Bottom + 0.01f);
    }

    [Fact]
    public void SnapToRatio_TargetBiggerThanImage_ShrinksToImage()
    {
        // 图 200x100 吸 1:1：可用最大 100x100
        var rect = new SKRect(0, 0, 200, 100);
        var snapped = CropRects.SnapToRatio(rect, 1, 1, Image);
        Assert.Equal(100f, snapped.Width, 2f);
        Assert.Equal(100f, snapped.Height, 2f);
    }

    [Fact]
    public void SnapToRatio_NullOrInvalid_ReturnsClampedOriginal()
    {
        var rect = new SKRect(5, 5, 50, 40);
        Assert.Equal(rect, CropRects.SnapToRatio(rect, 0, 0, Image));
        Assert.Equal(rect, CropRects.SnapToRatio(rect, -1, 3, Image));
    }

    // ---------- ClampToImage ----------

    [Fact]
    public void ClampToImage_IntersectsAndClamps()
    {
        var rect = new SKRect(-10, 20, 250, 80);
        var clamped = CropRects.ClampToImage(rect, Image)!.Value;
        Assert.Equal(0, clamped.Left);
        Assert.Equal(20, clamped.Top);
        Assert.Equal(200, clamped.Right);
        Assert.Equal(80, clamped.Bottom);
    }

    [Fact]
    public void ClampToImage_FullyOutside_ReturnsNull()
    {
        Assert.Null(CropRects.ClampToImage(new SKRect(300, 300, 400, 400), Image));
    }
}
