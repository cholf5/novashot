using SkiaSharp;
using Novashot.Core.Library;

namespace Novashot.Core.Tests;

/// <summary>
/// ThumbnailWriter（M7a）：SKBitmap → 最长边 320 等比缩放 PNG 落盘；小图不放大。
/// </summary>
public class ThumbnailWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-thumbnail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static SKBitmap MakeBitmap(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.Teal);
        return bmp;
    }

    [Fact]
    public void Write_ScalesLandscape_ToMaxEdge320()
    {
        Directory.CreateDirectory(_tempDir);
        using var bitmap = MakeBitmap(1600, 800);

        var path = ThumbnailWriter.Write(_tempDir, "thumb_abc.png", bitmap);

        Assert.Equal(Path.Combine(_tempDir, "thumb_abc.png"), path);
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded!.Width);
        Assert.Equal(160, decoded.Height);
    }

    [Fact]
    public void Write_ScalesPortrait_ToMaxEdge320()
    {
        Directory.CreateDirectory(_tempDir);
        using var bitmap = MakeBitmap(600, 1200);

        ThumbnailWriter.Write(_tempDir, "thumb_portrait.png", bitmap);

        using var decoded = SKBitmap.Decode(Path.Combine(_tempDir, "thumb_portrait.png"));
        Assert.NotNull(decoded);
        Assert.Equal(160, decoded!.Width);
        Assert.Equal(320, decoded.Height);
    }

    [Fact]
    public void Write_SmallBitmap_NotUpscaled()
    {
        Directory.CreateDirectory(_tempDir);
        using var bitmap = MakeBitmap(120, 80);

        ThumbnailWriter.Write(_tempDir, "thumb_small.png", bitmap);

        using var decoded = SKBitmap.Decode(Path.Combine(_tempDir, "thumb_small.png"));
        Assert.NotNull(decoded);
        Assert.Equal(120, decoded!.Width);
        Assert.Equal(80, decoded.Height);
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_tempDir, "thumbs", "sub");
        using var bitmap = MakeBitmap(100, 100);

        var path = ThumbnailWriter.Write(nested, "thumb_nested.png", bitmap);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MaxEdge_Is320()
    {
        Assert.Equal(320, ThumbnailWriter.MaxEdge);
    }
}
