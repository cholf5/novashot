using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Novashot.App.Assets;

/// <summary>
/// 应用图标（含托盘图标）在有资源文件之前，先由 Skia 程序化生成，避免拉入设计资产。
/// M6 会替换为设计好的 .ico。
/// </summary>
public static class AppIconFactory
{
    /// <summary>PNG 编码，供 Bitmap / WindowIcon 共用，避免 Avalonia Bitmap.Save 的 obsolete 重载。</summary>
    private static byte[] EncodePng(int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // 圆角紫色底
        using (var bg = new SKPaint { Color = new SKColor(0x66, 0x7E, 0xEA), IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            var rect = new SKRoundRect(new SKRect(2, 2, size - 2, size - 2), size * 0.22f);
            canvas.DrawRoundRect(rect, bg);
        }

        // 白色 P
        using (var text = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), size * 0.6f))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            var glyph = "P";
            var metrics = text.Metrics;
            var textWidth = text.MeasureText(glyph);
            var x = (size - textWidth) / 2f;
            var y = (size - metrics.Ascent - metrics.Descent) / 2f;
            canvas.DrawText(glyph, x, y, SKTextAlign.Left, text, paint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static Bitmap CreatePlaceholderIcon(int size = 64)
    {
        var bytes = EncodePng(size);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    public static WindowIcon CreatePlaceholderWindowIcon(int size = 64)
    {
        var bytes = EncodePng(size);
        using var ms = new MemoryStream(bytes);
        return new WindowIcon(ms);
    }
}
