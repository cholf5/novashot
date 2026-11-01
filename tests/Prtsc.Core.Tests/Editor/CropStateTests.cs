using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// M6d 非破坏 Crop 的管线语义：
/// - Default（无 crop）输出与既有渲染逐像素一致（回归锚点不破）；
/// - Measure 含 crop 尺寸；
/// - crop 外像素露出背景（或透明）；crop 内 = 源图对应区域平移；
/// - <b>裁剪裁标注</b>：越界标注被 clip（撤销 crop 即恢复，数据不动）。
/// </summary>
public class CropStateTests
{
    private static SKBitmap SolidSource(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static SKBitmap StripedSource(int w, int h)
    {
        var bmp = SolidSource(w, h, SKColors.Black);
        unsafe
        {
            var p = (uint*)bmp.GetPixels();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    p[y * w + x] = 0xFF000000u | ((uint)(y * 8) << 8) | (uint)(x * 3); // x 定 R，y 定 G
        }
        return bmp;
    }

    private static SKBitmap RenderExact(AnnotationDocument doc, EditorState state)
    {
        var size = Composition.Measure(doc, state);
        var result = new SKBitmap(new SKImageInfo(size.Width, size.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        Composition.RenderScene(canvas, doc, state);
        canvas.Flush();
        return result;
    }

    // ---------- 锚点 ----------

    [Fact]
    public void Default_WithNullCrop_MatchesLegacyPixelForPixel()
    {
        using var src = StripedSource(30, 20);
        var doc = new AnnotationDocument(src);
        doc.Add(new RectangleAnnotation(new SKRect(5, 5, 20, 15), AnnotationStyle.Default with { StrokeWidth = 3 }));

        using var legacy = new SKBitmap(new SKImageInfo(30, 20, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(legacy))
        {
            Composition.RenderScene(c, doc);
            c.Flush();
        }

        using var pipelined = RenderExact(doc, EditorState.Default);
        AssertSamePixels(legacy, pipelined);
    }

    // ---------- Measure ----------

    [Fact]
    public void Measure_UsesCropSize_PlusPadding()
    {
        using var src = SolidSource(100, 80, SKColors.Gray);
        var doc = new AnnotationDocument(src);
        var state = new EditorState(
            CropRect: new SKRect(10, 20, 60, 70), // 50x50
            Padding: 8);
        var size = Composition.Measure(doc, state);
        Assert.Equal(66, size.Width);
        Assert.Equal(66, size.Height);
    }

    // ---------- 渲染 ----------

    [Fact]
    public void Render_Crop_TranslatesSourcePixels()
    {
        using var src = StripedSource(40, 30);
        var doc = new AnnotationDocument(src);
        var state = new EditorState(CropRect: new SKRect(10, 5, 30, 20)); // 20x15

        using var bmp = RenderExact(doc, state);
        Assert.Equal(20, bmp.Width);
        Assert.Equal(15, bmp.Height);
        // 内容 = 源图 (10,5) 处像素
        Assert.Equal(src.GetPixel(10, 5), bmp.GetPixel(0, 0));
        Assert.Equal(src.GetPixel(29, 19), bmp.GetPixel(19, 14));
    }

    [Fact]
    public void Render_Crop_WithBackground_CornersShowBackground()
    {
        using var src = SolidSource(40, 30, SKColors.DimGray);
        var doc = new AnnotationDocument(src);
        var state = new EditorState(
            CropRect: new SKRect(10, 5, 30, 20),
            Background: BackgroundKind.SolidColor,
            BackgroundColor: SKColors.Red,
            Padding: 4);

        using var bmp = RenderExact(doc, state);
        Assert.Equal(28, bmp.Width);   // crop 20 + 2*4
        Assert.Equal(23, bmp.Height);  // crop 15 + 2*4
        Assert.Equal(SKColors.Red, bmp.GetPixel(0, 0));
        Assert.Equal(SKColors.DimGray, bmp.GetPixel(10, 10)); // 内容区 = 源图 (6,6)
    }

    [Fact]
    public void Render_Crop_ClipsAnnotations()
    {
        // 标注画在 crop 区域外 → 不可见；覆盖边界 → 只见框内部分
        using var src = SolidSource(60, 40, SKColors.White);
        var doc = new AnnotationDocument(src);
        doc.Add(new RectangleAnnotation(new SKRect(45, 10, 58, 30), AnnotationStyle.Default with { StrokeWidth = 2 }));
        doc.Add(new RectangleAnnotation(new SKRect(5, 5, 25, 15), AnnotationStyle.Default with { StrokeWidth = 2 }));

        var state = new EditorState(CropRect: new SKRect(0, 0, 40, 40));
        using (var bmp = RenderExact(doc, state))
        {
            // 右侧标注 (45..58) 完全在 crop (0..40) 外 → crop 后输出宽 40，它不可能出现
            Assert.Equal(40, bmp.Width);
        }

        // 撤销 crop（回到 Default）→ 标注重新可见（非破坏）：取样右边标注顶边描边 (50,10)
        using (var bmp = RenderExact(doc, EditorState.Default))
        {
            Assert.Equal(60, bmp.Width);
            Assert.NotEqual(SKColors.White, bmp.GetPixel(50, 10));
        }
    }

    [Fact]
    public void Render_CropWithAnnotations_DrawsAnnotationRelativeToCropOrigin()
    {
        using var src = SolidSource(60, 40, SKColors.White);
        var doc = new AnnotationDocument(src);
        // 实心矩形标注在 (10,5)-(25,15)
        doc.Add(new RectangleAnnotation(new SKRect(10, 5, 25, 15),
            AnnotationStyle.Default with { StrokeWidth = 4, Stroke = SKColors.Lime }));

        var state = new EditorState(CropRect: new SKRect(10, 5, 30, 20));
        using var bmp = RenderExact(doc, state);
        // 标注 (10,5) → 输出 (0,0)；(1,1) 落在宽 4 的 lime 描边上
        var c = bmp.GetPixel(1, 1);
        Assert.True(c.Green > 200, $"期望 lime 描边，实际 {c}");
    }

    [Fact]
    public void Clamped_CropOutsideImage_ReducesIntoImage()
    {
        using var src = SolidSource(50, 50, SKColors.Gray);
        var doc = new AnnotationDocument(src);
        var state = new EditorState(CropRect: new SKRect(20, 20, 120, 90)).Clamped();
        var size = Composition.Measure(doc, state);
        Assert.Equal(30, size.Width);
        Assert.Equal(30, size.Height);
    }

    // ---------- helpers ----------

    private static void AssertSamePixels(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(expected.GetPixel(x, y) == actual.GetPixel(x, y),
                    $"({x},{y}) expected {expected.GetPixel(x, y)} got {actual.GetPixel(x, y)}");
    }
}
