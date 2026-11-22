using SkiaSharp;

namespace Novashot.Core.Windows;

/// <summary>
/// B-2 WindowPicker 缩略图：等比缩放纯函数（App 侧负责 SKBitmap → Avalonia Bitmap 转换与失败降级）。
/// 口径：装进 maxW×maxH 的等比盒子；小图不放大；退化输入输出至少 1px。
/// </summary>
public static class PickerThumbnails
{
    public const int ThumbWidth = 96;
    public const int ThumbHeight = 54;

    /// <summary>等比缩到 ≤ maxW×maxH（不放大）。退化（非正数）返回 (1,1)。</summary>
    public static (int Width, int Height) FitSize(int srcW, int srcH, int maxW, int maxH)
    {
        if (srcW <= 0 || srcH <= 0) return (1, 1);

        var scale = Math.Min(1.0, Math.Min((double)maxW / srcW, (double)maxH / srcH));
        return (
            Math.Max(1, (int)Math.Round(srcW * scale)),
            Math.Max(1, (int)Math.Round(srcH * scale)));
    }

    /// <summary>按 FitSize 生成新 SKBitmap（透明底等比绘制），源位图不被改动、由调用方释放。</summary>
    public static SKBitmap Resize(SKBitmap source, int maxW, int maxH)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (width, height) = FitSize(source.Width, source.Height, maxW, maxH);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(SKMatrix.CreateScale((float)width / source.Width, (float)height / source.Height));
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();

        using var image = surface.Snapshot();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("PickerThumbnails.Resize: ReadPixels failed");
        }
        return bitmap;
    }
}
