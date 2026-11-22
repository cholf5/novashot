using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// 透明区域占位格（棋盘纹理）的像素级语义：只在给定矩形内落色、深浅交替、
/// 图案原点对齐矩形左上角（免得多边形/裁剪时格子看起来在滑动）。
/// </summary>
public class TransparencyCheckerTests
{
    private static readonly SKColor Light = TransparencyChecker.LightColor;
    private static readonly SKColor Dark = TransparencyChecker.DarkColor;

    /// <summary>哨兵底色：任何未被棋盘覆盖的像素都还是它。</summary>
    private static readonly SKColor Sentinel = SKColors.Magenta;

    private static SKBitmap NewCanvas(int w = 40, int h = 40)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(Sentinel);
        return bmp;
    }

    [Fact]
    public void Draw_paints_alternating_cells()
    {
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(0, 0, 16, 16), cellSize: 4f);
            canvas.Flush();
        }

        Assert.Equal(Light, bmp.GetPixel(1, 1));   // 第 1 格浅
        Assert.Equal(Dark, bmp.GetPixel(5, 1));    // 同行第 2 格深
        Assert.Equal(Light, bmp.GetPixel(9, 1));   // 交替回来
        Assert.Equal(Dark, bmp.GetPixel(1, 5));    // 下一行错位
        Assert.Equal(Light, bmp.GetPixel(5, 5));
    }

    [Fact]
    public void Draw_leaves_area_outside_rect_untouched()
    {
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(4, 4, 12, 12), cellSize: 4f);
            canvas.Flush();
        }

        Assert.Equal(Light, bmp.GetPixel(4, 4));
        Assert.Equal(Sentinel, bmp.GetPixel(2, 2));    // 矩形外
        Assert.Equal(Sentinel, bmp.GetPixel(14, 14));
        Assert.Equal(Sentinel, bmp.GetPixel(20, 8));
    }

    [Fact]
    public void Draw_anchors_pattern_to_rect_origin()
    {
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(5, 7, 21, 23), cellSize: 4f);
            canvas.Flush();
        }

        // 左上角永远是浅格——格子跟着矩形走，不跟画布原点走
        Assert.Equal(Light, bmp.GetPixel(5, 7));
        Assert.Equal(Dark, bmp.GetPixel(9, 7));
        Assert.Equal(Dark, bmp.GetPixel(5, 11));
    }

    [Fact]
    public void Draw_honours_cell_size()
    {
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(0, 0, 20, 20), cellSize: 10f);
            canvas.Flush();
        }

        Assert.Equal(Light, bmp.GetPixel(1, 1));
        Assert.Equal(Light, bmp.GetPixel(9, 9));
        Assert.Equal(Dark, bmp.GetPixel(11, 1));   // 第 2 格（10..20）才换色
        Assert.Equal(Dark, bmp.GetPixel(1, 11));
        Assert.Equal(Light, bmp.GetPixel(11, 11));
    }

    [Fact]
    public void Draw_is_noop_for_empty_rect()
    {
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(10, 10, 10, 10));
            TransparencyChecker.Draw(canvas, new SKRect(10, 10, 4, 10));
            canvas.Flush();
        }

        Assert.Equal(Sentinel, bmp.GetPixel(10, 10));
    }

    [Fact]
    public void Default_palette_is_dark_checker()
    {
        // 暗色 UI：占位格必须比画布衬底亮一点点，否则"看不见那里有东西"这个 bug 又回来了
        using var bmp = NewCanvas();
        using (var canvas = new SKCanvas(bmp))
        {
            TransparencyChecker.Draw(canvas, new SKRect(0, 0, 32, 32));
            canvas.Flush();
        }

        Assert.Equal(TransparencyChecker.LightColor, bmp.GetPixel(1, 1));
        Assert.Equal(TransparencyChecker.DarkColor, bmp.GetPixel(9, 1));
        Assert.True(TransparencyChecker.LightColor.Red > TransparencyChecker.DarkColor.Red);
        Assert.Equal(255, bmp.GetPixel(1, 1).Alpha); // 不透明：占位格不能自己变成"透明"的

        // 与画布衬底（SkiaCanvas 的 #1A1A1A）拉开足够差：太接近就又回到"什么都看不见"
        const int plate = 0x1A;
        Assert.True(TransparencyChecker.DarkColor.Red - plate >= 0x08,
            $"dark cell {TransparencyChecker.DarkColor} too close to the {plate:X2} plate");
        Assert.True(TransparencyChecker.LightColor.Red - plate >= 0x18,
            $"light cell {TransparencyChecker.LightColor} too close to the {plate:X2} plate");
    }
}
