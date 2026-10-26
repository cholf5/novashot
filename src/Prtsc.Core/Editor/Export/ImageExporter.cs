using Prtsc.Core.Annotation;
using SkiaSharp;

namespace Prtsc.Core.Editor.Export;

/// <summary>
/// 把 <see cref="AnnotationDocument"/> 离屏渲染并编码为 PNG / JPEG / WebP。
/// M8 CLI 的 headless 导出直接复用本类（不经 Avalonia）。
/// </summary>
public static class ImageExporter
{
    public const int DefaultQuality = 90;

    /// <summary>
    /// 离屏渲染文档内容（无预览、无跳过项），尺寸 = 文档像素尺寸。
    /// 返回的 <see cref="SKImage"/> 由调用方 Dispose。
    /// </summary>
    public static SKImage Render(AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var info = new SKImageInfo(document.Width, document.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create offscreen surface for export.");
        using (surface)
        {
            Composition.RenderScene(surface.Canvas, document);
            surface.Canvas.Flush();
            return surface.Snapshot();
        }
    }

    /// <summary>编码为指定格式。PNG 忽略 <paramref name="quality"/>；JPEG/WebP 为 1..100。
    /// 返回的 <see cref="SKData"/> 由调用方 Dispose。</summary>
    public static SKData Encode(AnnotationDocument document, ExportFormat format, int quality = DefaultQuality)
    {
        using var image = Render(document);
        var skFormat = format switch
        {
            ExportFormat.Png  => SKEncodedImageFormat.Png,
            ExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ExportFormat.WebP => SKEncodedImageFormat.Webp,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
        return image.Encode(skFormat, Math.Clamp(quality, 1, 100));
    }

    /// <summary>渲染 + 编码 + 落盘。目录不存在会抛 <see cref="DirectoryNotFoundException"/>。</summary>
    public static void Save(AnnotationDocument document, string filePath, ExportFormat format, int quality = DefaultQuality)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is empty.", nameof(filePath));
        using var data = Encode(document, format, quality);
        using var stream = File.Create(filePath);
        data.SaveTo(stream);
    }

    /// <summary>按扩展名推断格式；未知/无扩展名默认 PNG。</summary>
    public static ExportFormat FormatFromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp"           => ExportFormat.WebP,
            _                 => ExportFormat.Png,
        };
    }

    /// <summary>格式 → 首选扩展名（含点，小写）。</summary>
    public static string ExtensionFor(ExportFormat format) => format switch
    {
        ExportFormat.Png  => ".png",
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.WebP => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
