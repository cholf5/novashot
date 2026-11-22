using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class InvertAnnotationTests
{
    private static SKBitmap MakeSource(int w = 8, int h = 8)
    {
        var bmp = new SKBitmap(w, h);
        bmp.Erase(new SKColor(255, 255, 255));
        return bmp;
    }

    [Fact]
    public void Render_InvertsPixelsInsideRegion_KeepsOutside()
    {
        using var source = MakeSource();
        var region = new SKRect(2, 2, 6, 6);
        var ann = new InvertAnnotation(region, source, AnnotationStyle.Default);

        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
        surface.Canvas.Clear(SKColors.White); // 模拟真实管线：标注画在文档（源图）之上
        ann.Render(surface.Canvas);

        using var snapshot = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(snapshot);
        Assert.Equal(new SKColor(0, 0, 0), bmp.GetPixel(3, 3));       // 区域内：白→黑
        Assert.Equal(new SKColor(255, 255, 255), bmp.GetPixel(0, 0)); // 区域外：不变
    }

    [Fact]
    public void RegionClampsToSource_NoThrow()
    {
        using var source = MakeSource();
        var ann = new InvertAnnotation(new SKRect(4, 4, 20, 20), source, AnnotationStyle.Default);
        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
        // 不抛异常即通过（采样时钳回源边界）
        ann.Render(surface.Canvas);
    }
}
