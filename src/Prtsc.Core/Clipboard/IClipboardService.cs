using SkiaSharp;

namespace Prtsc.Core.Clipboard;

/// <summary>
/// 平台无关的位图剪贴板能力（M6c）。由 Prtsc.Platform.Windows 提供 Win32 实现；
/// 测试用内存 Fake。语义：
/// - <see cref="TrySetImage"/>：把不透明位图放到剪贴板（内部转 CF_DIBV5 + CF_DIB）。拷贝即快照，调用方保有位图所有权。
/// - <see cref="TryGetImage"/>：取回剪贴板位图（DIB 格式）；无位图/格式不符返回 null。返回值归调用方 Dispose。
/// </summary>
public interface IClipboardService
{
    bool TrySetImage(SKBitmap bitmap);

    SKBitmap? TryGetImage();

    /// <summary>写入纯文本（M7b：上传成功后复制 URL）。成功返回 true。</summary>
    bool TrySetText(string text);
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

    private string? _text;
}
