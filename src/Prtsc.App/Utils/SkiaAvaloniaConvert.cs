using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Prtsc.App.Utils;

/// <summary>
/// SkiaSharp SKBitmap 与 Avalonia Bitmap 之间的转换助手。
/// </summary>
public static class SkiaAvaloniaConvert
{
    /// <summary>
    /// 通过 PNG 编码往返转成 Avalonia Bitmap。适合一次性帧展示；
    /// 对逐帧渲染场景建议使用直接 Skia 绘制（M2+ 会替换）。
    /// </summary>
    public static Bitmap ToAvaloniaBitmap(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
