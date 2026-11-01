using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// Auto edge color：采样（裁剪后的）图像边缘带 → 量化直方图 → 主色。
/// 用于把截图边缘主色一键设为合成背景（与 EditorState.BackgroundColor 打通）。
/// </summary>
public class EdgeColorExtractorTests
{
    private static SKBitmap Solid(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    [Fact]
    public void Extract_SolidImage_ReturnsThatColor()
    {
        using var bmp = Solid(20, 20, new SKColor(0x66, 0x7E, 0xEA));
        var doc = new AnnotationDocument(bmp);
        var color = EdgeColorExtractor.Extract(doc);
        Assert.Equal(new SKColor(0x66, 0x7E, 0xEA), color);
    }

    [Fact]
    public void Extract_NoisyCenter_EdgeDominantWins()
    {
        // 边缘统一青色，中心画噪点：主色仍应是青
        using var bmp = Solid(40, 40, new SKColor(0, 0xB0, 0xB0));
        var doc = new AnnotationDocument(bmp);
        var rnd = new Random(42);
        for (int i = 0; i < 80; i++)
            bmp.SetPixel(rnd.Next(10, 30), rnd.Next(10, 30), SKColors.White);

        var color = EdgeColorExtractor.Extract(doc);
        Assert.Equal(new SKColor(0, 0xB0, 0xB0), color);
    }

    [Fact]
    public void Extract_UsesCropRect_WhenPresent()
    {
        // 左半红右半蓝；crop 只留右半 → 蓝
        using var bmp = Solid(40, 20, SKColors.Red);
        for (int y = 0; y < 20; y++)
            for (int x = 20; x < 40; x++)
                bmp.SetPixel(x, y, SKColors.Blue);
        var doc = new AnnotationDocument(bmp);

        var cropped = new EditorState(CropRect: new SKRect(20, 0, 40, 20));
        Assert.Equal(SKColors.Blue, EdgeColorExtractor.Extract(doc, cropped));
        Assert.Equal(SKColors.Red, EdgeColorExtractor.Extract(doc, EditorState.Default));
    }

    [Fact]
    public void Extract_DegenerateImage_ReturnsFallback()
    {
        using var bmp = Solid(2, 2, SKColors.Gray);
        var doc = new AnnotationDocument(bmp);
        var color = EdgeColorExtractor.Extract(doc);
        Assert.Equal(SKColors.Gray, color);
    }

    [Fact]
    public void Extract_NullSource_ReturnsWhite()
    {
        // 防御路径：文档无源图时给白色兜底
        Assert.Equal(SKColors.White, EdgeColorExtractor.Extract(null!));
    }
}
