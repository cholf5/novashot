using Novashot.Core.Utils;
using SkiaSharp;

namespace Novashot.Core.Library;

/// <summary>
/// 缩略图生成：SKBitmap → 最长边 320 的等比缩放 PNG 落盘（小图不放大）。
/// 文件名由调用方生成（建议 thumb_{entryId}.png）。
/// </summary>
public static class ThumbnailWriter
{
    public const int MaxEdge = 320;

    /// <summary>生成缩略图并写入目录，返回完整路径。目录不存在会自动创建。</summary>
    public static string Write(string directory, string fileName, SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is empty.", nameof(fileName));

        System.IO.Directory.CreateDirectory(directory);

        var scale = Math.Min(1.0, (double)MaxEdge / Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(SKMatrix.CreateScale((float)width / bitmap.Width, (float)height / bitmap.Height));
        // 非拥有包装 + 平滑采样（见 Utils.Sampling）：nearest 深缩小会让缩略图摩尔纹
        using var srcView = SKImage.FromPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes);
        canvas.DrawImage(srcView, 0, 0, Sampling.Smooth, null);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        var path = Path.Combine(directory, fileName);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
