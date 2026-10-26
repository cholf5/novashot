using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Editor.Export;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class ImageExporterTests
{
    private static SKBitmap SolidSource(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static AnnotationDocument DocWithRedRect(int w = 40, int h = 30)
    {
        var bmp = SolidSource(w, h, SKColors.White);
        var doc = new AnnotationDocument(bmp);
        doc.Add(new RectangleAnnotation(new SKRect(10, 10, 20, 20),
            new AnnotationStyle(new SKColor(255, 0, 0), 2f, Fill: new SKColor(255, 0, 0))));
        return doc; // 测试进程内不 Dispose 位图（GC 兜底），避免额外包装
    }

    [Fact]
    public void Png_encode_decodes_back_with_same_size()
    {
        var doc = DocWithRedRect(40, 30);
        using var data = ImageExporter.Encode(doc, ExportFormat.Png);
        Assert.True(data.Size > 0);

        using var decoded = SKBitmap.Decode(data.AsSpan());
        Assert.NotNull(decoded);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(30, decoded.Height);
    }

    [Fact]
    public void Png_export_includes_annotations()
    {
        var doc = DocWithRedRect(40, 30);
        using var data = ImageExporter.Encode(doc, ExportFormat.Png);
        using var decoded = SKBitmap.Decode(data.AsSpan());

        Assert.Equal(new SKColor(255, 0, 0), decoded!.GetPixel(15, 15));
        Assert.Equal(SKColors.White, decoded.GetPixel(3, 3));
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg)]
    [InlineData(ExportFormat.WebP)]
    public void Lossy_formats_decode_back_with_same_size(ExportFormat format)
    {
        var doc = DocWithRedRect(32, 24);
        using var data = ImageExporter.Encode(doc, format, quality: 80);
        Assert.True(data.Size > 0);

        using var decoded = SKBitmap.Decode(data.AsSpan());
        Assert.NotNull(decoded);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(24, decoded.Height);
    }

    [Fact]
    public void Render_returns_image_of_document_size()
    {
        var doc = DocWithRedRect(17, 13);
        using var image = ImageExporter.Render(doc);
        Assert.Equal(17, image.Width);
        Assert.Equal(13, image.Height);
    }

    [Theory]
    [InlineData("x.png", ExportFormat.Png)]
    [InlineData("X.PNG", ExportFormat.Png)]
    [InlineData("x.jpg", ExportFormat.Jpeg)]
    [InlineData("x.JPEG", ExportFormat.Jpeg)]
    [InlineData("x.webp", ExportFormat.WebP)]
    [InlineData("x.txt", ExportFormat.Png)]   // 未知扩展名 → 默认 PNG
    [InlineData("noext", ExportFormat.Png)]
    public void FormatFromExtension_maps_and_defaults_to_png(string path, ExportFormat expected)
        => Assert.Equal(expected, ImageExporter.FormatFromExtension(path));

    [Theory]
    [InlineData(ExportFormat.Png, ".png")]
    [InlineData(ExportFormat.Jpeg, ".jpg")]
    [InlineData(ExportFormat.WebP, ".webp")]
    public void ExtensionFor_maps_formats(ExportFormat format, string expected)
        => Assert.Equal(expected, ImageExporter.ExtensionFor(format));

    [Fact]
    public void Save_writes_nonempty_file()
    {
        var doc = DocWithRedRect(20, 20);
        var path = Path.Combine(Path.GetTempPath(), $"prtsc-test-{Guid.NewGuid():N}.png");
        try
        {
            ImageExporter.Save(doc, path, ExportFormat.Png);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);

            using var decoded = SKBitmap.Decode(File.ReadAllBytes(path));
            Assert.Equal(20, decoded!.Width);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
