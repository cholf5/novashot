using SkiaSharp;
using Prtsc.Core.Common;

namespace Prtsc.Core.Capture;

/// <summary>
/// 一次捕获的结果。持有 SKBitmap 所有权；使用完后调用 Dispose。
/// </summary>
public sealed class CapturedImage : IDisposable
{
    public SKBitmap Bitmap { get; }
    public PixelRect Bounds { get; }
    public double ScaleFactor { get; }
    public DateTime CapturedAt { get; }
    public CaptureSource Source { get; }

    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;

    private bool _disposed;

    public CapturedImage(SKBitmap bitmap, PixelRect bounds, double scaleFactor, CaptureSource source)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Bounds = bounds;
        ScaleFactor = scaleFactor;
        Source = source;
        CapturedAt = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Bitmap.Dispose();
    }
}

public enum CaptureSource
{
    Fullscreen,
    Display,
    Region,
    Window,
    Clipboard,
    File
}
