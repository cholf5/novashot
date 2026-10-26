using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// Mosaic / Blur / Pencil / Highlighter 的 CPU 光栅渲染测试：直接在 SKBitmap 上开 SKCanvas
/// 渲染标注，断言像素级性质（像素化均匀性、区域外不变、混合模式正确）。
/// </summary>
public class PixelEffectRenderTests
{
    private static readonly AnnotationStyle Style = AnnotationStyle.Default;

    /// <summary>x 方向线性渐变（0..240 红）的 16×16 测试图。</summary>
    private static SKBitmap GradientSource(int w = 16, int h = 16)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        for (int x = 0; x < w; x++)
        {
            using var p = new SKPaint { Color = new SKColor((byte)(x * 16), 0, 0) };
            canvas.DrawRect(x, 0, x + 1, h, p);
        }
        return bmp;
    }

    /// <summary>上半红、下半蓝的 16×16 测试图。</summary>
    private static SKBitmap SplitSource(int w = 16, int h = 16)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        using var red = new SKPaint { Color = new SKColor(255, 0, 0) };
        using var blue = new SKPaint { Color = new SKColor(0, 0, 255) };
        canvas.DrawRect(0, 0, w, h / 2f, red);
        canvas.DrawRect(0, h / 2f, w, h, blue);
        return bmp;
    }

    private static SKBitmap Render(SKBitmap source, AnnotationBase annotation)
    {
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source, 0, 0);
        annotation.Render(canvas);
        canvas.Flush();
        return result;
    }

    // ---- Mosaic ----

    [Fact]
    public void Mosaic_makes_each_block_uniform_and_blocks_differ()
    {
        using var src = GradientSource();
        var mosaic = new MosaicAnnotation(new SKRect(0, 0, 16, 16), src, Style, blockSize: 4);
        using var result = Render(src, mosaic);

        // 同一 4×4 块内像素一致（像素化性质）
        Assert.Equal(result.GetPixel(0, 0), result.GetPixel(3, 0));
        Assert.Equal(result.GetPixel(0, 0), result.GetPixel(0, 3));
        Assert.Equal(result.GetPixel(0, 0), result.GetPixel(3, 3));
        // 相邻块颜色不同（渐变被粗化）
        Assert.NotEqual(result.GetPixel(0, 0), result.GetPixel(4, 0));
        Assert.NotEqual(result.GetPixel(12, 12), result.GetPixel(4, 4));
    }

    [Fact]
    public void Mosaic_leaves_pixels_outside_region_untouched()
    {
        using var src = GradientSource();
        var mosaic = new MosaicAnnotation(new SKRect(0, 0, 8, 16), src, Style, blockSize: 4);
        using var result = Render(src, mosaic);

        // 区域外（右半）逐像素不变
        for (int y = 0; y < 16; y++)
            Assert.Equal(src.GetPixel(12, y), result.GetPixel(12, y));
        // 区域内被像素化：x=0 与 x=3 同块一致
        Assert.Equal(result.GetPixel(0, 5), result.GetPixel(3, 5));
    }

    [Fact]
    public void Mosaic_out_of_bounds_region_is_clamped()
    {
        using var src = GradientSource();
        // 越界区域：clamp 后仍渲染不抛异常且块内均匀
        var mosaic = new MosaicAnnotation(new SKRect(8, 8, 64, 64), src, Style, blockSize: 8);
        using var result = Render(src, mosaic);
        Assert.Equal(result.GetPixel(8, 8), result.GetPixel(12, 12));
    }

    // ---- Blur ----

    [Fact]
    public void Blur_blends_boundary_inside_region_and_keeps_outside_untouched()
    {
        using var src = SplitSource();
        var blur = new BlurAnnotation(new SKRect(0, 0, 16, 8), src, Style, sigma: 8f);
        using var result = Render(src, blur);

        var pureRed = new SKColor(255, 0, 0);
        var pureBlue = new SKColor(0, 0, 255);

        // 区域内靠近红/蓝分界：不再是纯红
        var edge = result.GetPixel(8, 7);
        Assert.NotEqual(pureRed, edge);
        Assert.True(edge.Red < 255); // 混入了蓝侧（B 分量抬升）
        Assert.True(edge.Blue > 0);

        // 区域外（下半）逐像素不变
        for (int x = 0; x < 16; x++)
            Assert.Equal(pureBlue, result.GetPixel(x, 12));
    }

    [Fact]
    public void Blur_of_uniform_area_keeps_color()
    {
        // 全图同色 → 模糊后不变（高斯核归一化）
        using var src = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Opaque));
        src.Erase(new SKColor(10, 200, 30));
        var blur = new BlurAnnotation(new SKRect(0, 0, 16, 16), src, Style, sigma: 6f);
        using var result = Render(src, blur);

        // 高斯核归一化；Skia 预乘通道舍入允许 ±2
        Assert.True(Math.Abs(src.GetPixel(8, 8).Red - result.GetPixel(8, 8).Red) <= 2);
        Assert.True(Math.Abs(src.GetPixel(3, 12).Green - result.GetPixel(3, 12).Green) <= 2);
    }

    // ---- Pencil / Highlighter ----

    [Fact]
    public void Pencil_draws_opaque_stroke()
    {
        var bmp = new SKBitmap(new SKImageInfo(100, 60, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.White);
        var pencil = new PencilAnnotation(new[] { new SKPoint(20, 30), new SKPoint(80, 30) },
            Style with { Stroke = new SKColor(200, 0, 0), StrokeWidth = 6 });
        using var result = Render(bmp, pencil);

        var c = result.GetPixel(50, 30);
        Assert.True(c.Red > 150, $"expected red stroke, got {c}");
        Assert.True(c.Green < 80, $"expected red stroke, got {c}");
    }

    [Fact]
    public void Highlighter_multiplies_over_white_background()
    {
        var bmp = new SKBitmap(new SKImageInfo(100, 60, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.White);
        var yellow = new SKColor(0xFC, 0xD3, 0x4D);
        var high = new HighlighterAnnotation(new[] { new SKPoint(20, 30), new SKPoint(80, 30) },
            Style with { Stroke = yellow, StrokeWidth = 2 });
        using var result = Render(bmp, high);

        // Multiply：黄 × 白 = 黄（笔迹中心）
        var c = result.GetPixel(50, 30);
        Assert.True(c.Red > 220 && c.Green > 190 && c.Blue < 120, $"expected yellow mark, got {c}");
        // 远处不受影响
        Assert.Equal(SKColors.White, result.GetPixel(50, 5));
    }
}
