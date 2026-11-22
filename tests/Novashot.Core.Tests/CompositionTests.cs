using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// 共享合成管线的渲染语义：源图直绘、标注 z 序叠加、skipAnnotation 跳过、preview 顶层。
/// 用 CPU 光栅（SKCanvas over SKBitmap）断言像素。
/// </summary>
public class CompositionTests
{
    private static SKBitmap SolidSource(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static AnnotationStyle RedFillStyle(float stroke = 2f) =>
        new(new SKColor(255, 0, 0), stroke, Fill: new SKColor(255, 0, 0));

    private static SKBitmap RenderTo(AnnotationDocument doc,
        AnnotationBase? preview = null, AnnotationBase? skip = null)
    {
        var result = new SKBitmap(new SKImageInfo(doc.Width, doc.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        Composition.RenderScene(canvas, doc, preview, skip);
        canvas.Flush();
        return result;
    }

    [Fact]
    public void Renders_source_without_annotations()
    {
        using var src = SolidSource(20, 10, SKColors.White);
        var doc = new AnnotationDocument(src);
        using var result = RenderTo(doc);
        Assert.Equal(SKColors.White, result.GetPixel(5, 5));
        Assert.Equal(SKColors.White, result.GetPixel(15, 8));
    }

    [Fact]
    public void Scaled_render_uses_smooth_sampling_not_nearest()
    {
        // 预览视口几乎总是非 1:1 缩放：1px 黑白竖条纹缩到 0.45× 后，nearest 采样只会
        // 输出纯黑/纯白（高频细节嚼成摩尔纹，4K@150% 预览马赛克的根因），平滑采样
        // 必然混出中间灰度。锁住源图重采样口径不回退到最近邻。
        var src = new SKBitmap(new SKImageInfo(200, 8, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (var x = 0; x < 200; x++)
            for (var y = 0; y < 8; y++)
                src.SetPixel(x, y, x % 2 == 0 ? SKColors.Black : SKColors.White);
        var doc = new AnnotationDocument(src);

        using var surface = SKSurface.Create(new SKImageInfo(90, 4, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Scale(0.45f);
        Composition.RenderScene(canvas, doc);
        canvas.Flush();

        using var snap = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(snap);
        var hasMidGray = false;
        for (var x = 0; x < bmp.Width && !hasMidGray; x++)
            for (var y = 0; y < bmp.Height && !hasMidGray; y++)
                if (bmp.GetPixel(x, y).Red is > 10 and < 245)
                    hasMidGray = true;
        Assert.True(hasMidGray, "非 1:1 缩放渲染应混出中间灰度（平滑采样），而不是 nearest 的纯黑白");
    }

    [Fact]
    public void Renders_annotation_on_top_of_source()
    {
        using var src = SolidSource(40, 40, SKColors.White);
        var doc = new AnnotationDocument(src);
        doc.Add(new RectangleAnnotation(new SKRect(10, 10, 30, 30), RedFillStyle()));

        using var result = RenderTo(doc);
        Assert.Equal(new SKColor(255, 0, 0), result.GetPixel(20, 20));   // Fill 区域
        Assert.Equal(SKColors.White, result.GetPixel(5, 5));             // 区域外仍白
    }

    [Fact]
    public void Skip_annotation_is_not_rendered()
    {
        using var src = SolidSource(40, 40, SKColors.White);
        var doc = new AnnotationDocument(src);
        var a = new RectangleAnnotation(new SKRect(5, 5, 15, 15), RedFillStyle());
        var b = new RectangleAnnotation(new SKRect(25, 25, 35, 35), RedFillStyle());
        doc.Add(a);
        doc.Add(b);

        using var result = RenderTo(doc, skip: a);
        Assert.Equal(SKColors.White, result.GetPixel(10, 10));           // a 被跳过
        Assert.Equal(new SKColor(255, 0, 0), result.GetPixel(30, 30));   // b 正常渲染
    }

    [Fact]
    public void Preview_renders_on_top()
    {
        using var src = SolidSource(40, 40, SKColors.White);
        var doc = new AnnotationDocument(src);
        var committed = new RectangleAnnotation(new SKRect(5, 5, 35, 35), RedFillStyle());
        var preview = new RectangleAnnotation(new SKRect(10, 10, 30, 30),
            new AnnotationStyle(new SKColor(0, 0, 255), 2f, Fill: new SKColor(0, 0, 255)));
        doc.Add(committed);

        using var result = RenderTo(doc, preview: preview);
        // preview 盖在 committed 之上：中心蓝、preview 外 committed 仍红
        Assert.Equal(new SKColor(0, 0, 255), result.GetPixel(20, 20));
        Assert.Equal(new SKColor(255, 0, 0), result.GetPixel(7, 7));
    }
}
