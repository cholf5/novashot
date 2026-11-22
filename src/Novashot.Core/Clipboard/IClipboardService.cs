using SkiaSharp;

namespace Novashot.Core.Clipboard;

/// <summary>
/// 平台无关的位图剪贴板能力（M6c）。由 Novashot.Platform.Windows 提供 Win32 实现；
/// 测试用内存 Fake。语义：
/// - <see cref="TrySetImage"/>：把不透明位图放到剪贴板（Windows 内部转 CF_DIBV5 + CF_DIB，
///   B-1 起像素 ≤4MP 时另尽力附注册格式 "PNG"——只认 PNG 字节的读者互操作，alpha 也得以保留；超界只放 DIB，I-1 阈值）。
///   拷贝即快照，调用方保有位图所有权。
/// - <see cref="TryGetImage"/>：取回剪贴板位图（DIB 优先；缺 DIB 或解不动时回退注册格式 "PNG"，B-1）；
///   无位图/格式不符返回 null。返回值归调用方 Dispose。
/// </summary>
public interface IClipboardService
{
    bool TrySetImage(SKBitmap bitmap);

    SKBitmap? TryGetImage();

    /// <summary>写入纯文本（M7b：上传成功后复制 URL）。成功返回 true。</summary>
    bool TrySetText(string text);

    /// <summary>
    /// G-11（winshot）：剪贴板上是文件列表（CF_HDROP，资源管理器“复制文件”）时，
    /// 返回第一个图片扩展名的文件完整路径；否则 null。
    /// </summary>
    string? TryGetImageFilePath();
}

/// <summary>进程内内存实现：App VM 单元测试用。</summary>
public sealed class FakeClipboardService : IClipboardService
{
    private SKBitmap? _bitmap;

    public bool TrySetImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _bitmap?.Dispose();
        _bitmap = bitmap.Copy(); // 快照语义
        return true;
    }

    public SKBitmap? TryGetImage() => _bitmap?.Copy();

    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        _text = text;
        return true;
    }

    public string? TryGetText() => _text;

    public string? TryGetImageFilePath() => _imageFilePath;

    private string? _text;

    /// <summary>测试注入：模拟 CF_HDROP 里的图片文件路径（null = 剪贴板无文件列表）。</summary>
    public string? ImageFilePath { get => _imageFilePath; set => _imageFilePath = value; }

    private string? _imageFilePath;
}
