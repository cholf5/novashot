using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// E-1 M9 屏幕取色器：物理像素位图上的取色纯逻辑。
/// 采样语义（batch 简报裁决）：overlay 手里的 CapturedImage.Bitmap 即物理像素，
/// 按 image 空间坐标直接采样，不做任何 DPI 换算——越界一律钳制回界内（放大镜贴边不崩）。
/// </summary>
public class ColorSamplerTests
{
    private static SKBitmap MakeBitmap(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.White);
        return bmp;
    }

    private static SKBitmap Gradient(int w, int h)
    {
        var bmp = MakeBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, new SKColor((byte)(x * 7 % 256), (byte)(y * 11 % 256), 0x42));
        return bmp;
    }

    // ---- Sample：单点采样 + 越界钳制 ----

    [Fact]
    public void Sample_CenterPixel_ReturnsExactColor()
    {
        using var bmp = Gradient(32, 32);
        var expected = bmp.GetPixel(10, 20);
        Assert.Equal(expected, ColorSampler.Sample(bmp, 10, 20));
    }

    [Fact]
    public void Sample_NegativeCoords_ClampToOrigin()
    {
        using var bmp = Gradient(32, 32);
        Assert.Equal(bmp.GetPixel(0, 0), ColorSampler.Sample(bmp, -5, -3));
        Assert.Equal(bmp.GetPixel(0, 0), ColorSampler.Sample(bmp, int.MinValue, int.MinValue));
    }

    [Fact]
    public void Sample_CoordsBeyondBounds_ClampToFarCorner()
    {
        using var bmp = Gradient(32, 32);
        Assert.Equal(bmp.GetPixel(31, 31), ColorSampler.Sample(bmp, 32, 40));
        Assert.Equal(bmp.GetPixel(31, 31), ColorSampler.Sample(bmp, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void Sample_MixedOverflow_ClampsEachAxisIndependently()
    {
        using var bmp = Gradient(32, 32);
        Assert.Equal(bmp.GetPixel(0, 31), ColorSampler.Sample(bmp, -1, 100));
        Assert.Equal(bmp.GetPixel(31, 0), ColorSampler.Sample(bmp, 100, -1));
    }

    [Fact]
    public void Sample_SolidBitmap_ReturnsThatColorAnywhere()
    {
        using var bmp = MakeBitmap(8, 8);
        bmp.Erase(new SKColor(0x66, 0x7E, 0xEA));
        Assert.Equal(new SKColor(0x66, 0x7E, 0xEA), ColorSampler.Sample(bmp, 3, 3));
        Assert.Equal(new SKColor(0x66, 0x7E, 0xEA), ColorSampler.Sample(bmp, 99, -99));
    }

    // ---- SampleNeighborhood：11×11 邻域 + 贴边裁剪 ----

    [Fact]
    public void Neighborhood_RadiusConstant_IsFive_ForElevenByEleven()
    {
        Assert.Equal(5, ColorSampler.NeighborhoodRadius);
        Assert.Equal(11, 2 * ColorSampler.NeighborhoodRadius + 1);
    }

    [Fact]
    public void Neighborhood_CenterAwayFromEdges_ReturnsRowMajor11x11()
    {
        using var bmp = Gradient(32, 32);
        var colors = ColorSampler.SampleNeighborhood(bmp, 16, 16, ColorSampler.NeighborhoodRadius);

        Assert.Equal(121, colors.Length);
        for (int row = 0; row < 11; row++)
            for (int col = 0; col < 11; col++)
                Assert.Equal(bmp.GetPixel(16 - 5 + col, 16 - 5 + row), colors[row * 11 + col]);
    }

    [Fact]
    public void Neighborhood_AtTopLeft_ClampsOutOfBoundsToNearestPixel()
    {
        // 中心 (0,0)：越界的半边全部钳制到 (0,0) 自身——贴边放大镜不崩、不空窗
        using var bmp = Gradient(32, 32);
        var colors = ColorSampler.SampleNeighborhood(bmp, 0, 0, ColorSampler.NeighborhoodRadius);

        Assert.Equal(121, colors.Length);
        for (int row = 0; row < 11; row++)
            for (int col = 0; col < 11; col++)
            {
                var expected = bmp.GetPixel(
                    Math.Clamp(col - 5, 0, 31),
                    Math.Clamp(row - 5, 0, 31));
                Assert.Equal(expected, colors[row * 11 + col]);
            }
    }

    [Fact]
    public void Neighborhood_AtBottomRight_ClampsOutOfBoundsToNearestPixel()
    {
        using var bmp = Gradient(32, 32);
        var colors = ColorSampler.SampleNeighborhood(bmp, 31, 31, ColorSampler.NeighborhoodRadius);

        for (int row = 0; row < 11; row++)
            for (int col = 0; col < 11; col++)
            {
                var expected = bmp.GetPixel(
                    Math.Clamp(31 + col - 5, 0, 31),
                    Math.Clamp(31 + row - 5, 0, 31));
                Assert.Equal(expected, colors[row * 11 + col]);
            }
    }

    [Fact]
    public void Neighborhood_CenterElement_IsTheCenterPixel()
    {
        using var bmp = Gradient(32, 32);
        var colors = ColorSampler.SampleNeighborhood(bmp, 10, 21, ColorSampler.NeighborhoodRadius);
        Assert.Equal(bmp.GetPixel(10, 21), colors[5 * 11 + 5]);
    }

    [Fact]
    public void Neighborhood_TinyBitmap_EverythingClamps()
    {
        // 1×1 位图：所有 121 个采样都钳回唯一像素
        using var bmp = MakeBitmap(1, 1);
        bmp.Erase(SKColors.Orange);
        var colors = ColorSampler.SampleNeighborhood(bmp, 0, 0, ColorSampler.NeighborhoodRadius);
        Assert.Equal(121, colors.Length);
        Assert.All(colors, c => Assert.Equal(SKColors.Orange, c));
    }

    [Fact]
    public void Neighborhood_NullBitmap_ReturnsEmpty()
    {
        // 防御路径：overlay 还没拿到位图时不渲染放大镜
        Assert.Empty(ColorSampler.SampleNeighborhood(null!, 5, 5, ColorSampler.NeighborhoodRadius));
    }
}
