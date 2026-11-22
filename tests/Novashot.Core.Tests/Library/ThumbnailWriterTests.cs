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

    [Fact]
    public void Write_DownscaleUsesSmoothSampling_NotNearest()
    {
        // 与 CompositionTests 同口径：1px 黑白竖条纹深缩小，nearest 只输出纯黑白，
        // 平滑采样必然混出中间灰度。锁住缩略图缩放不回退到最近邻。
        Directory.CreateDirectory(_tempDir);
        using var bitmap = new SKBitmap(new SKImageInfo(1600, 100, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            for (var x = 0; x < 1600; x += 2) canvas.DrawRect(x, 0, 1, 100, paint);
        }

        ThumbnailWriter.Write(_tempDir, "thumb_sampling.png", bitmap);

        using var decoded = SKBitmap.Decode(Path.Combine(_tempDir, "thumb_sampling.png"));
        Assert.NotNull(decoded);
        var hasMidGray = false;
        for (var x = 0; x < decoded!.Width && !hasMidGray; x++)
            for (var y = 0; y < decoded.Height && !hasMidGray; y++)
                if (decoded.GetPixel(x, y).Red is > 10 and < 245)
                    hasMidGray = true;
        Assert.True(hasMidGray, "缩略图缩小应平滑采样（出现中间灰度），而不是 nearest 的纯黑白");
    }
}
