using Microsoft.Extensions.Logging;
using SkiaSharp;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Platform.Mac.Interop;

namespace Prtsc.Platform.Mac.Capture;

/// <summary>
/// CoreGraphics 抓帧实现（语义对齐 GdiCaptureService：同步、整虚拟桌面、物理像素）。
/// 抓屏 CGDisplayCreateImage / 抓窗 CGWindowListCreateImage（天然含遮挡内容，无需先置顶）。
/// 屏幕录制权限未授予时 CG 返回 null：抛 InvalidOperationException 带指引文案。
/// </summary>
public sealed class MacCaptureService : ICaptureService
{
    private readonly IMacDisplayApi _api;
    private readonly ILogger<MacCaptureService> _logger;

    public MacCaptureService(IMacDisplayApi api, ILogger<MacCaptureService> logger)
    {
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// 一次枚举的快照：DisplayInfo 与 CG displayId 配对，全部是局部数据、无共享可变状态
    /// （review I-2：旧实现把 rawId 存实例字段 List，UI 线程热键抓帧与 CLI 转发后台线程抓帧
    /// 并发时互相覆写 → 张冠李戴/IndexOutOfRange。L-1 起捕获可跑在任意线程，快照语义是结构性修复）。
    /// </summary>
    private sealed record DisplaySnapshot(DisplayInfo Info, uint RawId);

    private DisplaySnapshot[] EnumerateSnapshot()
    {
        var raws = _api.EnumerateDisplays();
        var sorted = MacDisplayGeometry.SortForIndexing(raws);
        var snapshot = new DisplaySnapshot[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            snapshot[i] = new DisplaySnapshot(
                MacDisplayGeometry.ToDisplayInfo(sorted[i], MacDisplayGeometry.DeriveScaleFactor(sorted[i]), i),
                sorted[i].Id);
        }
        return snapshot;
    }

    private static DisplayInfo[] Infos(DisplaySnapshot[] snapshot)
    {
        var displays = new DisplayInfo[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++) displays[i] = snapshot[i].Info;
        return displays;
    }

    public IReadOnlyList<DisplayInfo> EnumerateDisplays()
    {
        // 每次返回新数组：调用方会在枚举返回值的过程中再次触发本方法（如 Region 流程逐屏 CaptureDisplay）；
        // 快照语义天然满足（无内部活缓存可被第二次枚举改动）
        return Infos(EnumerateSnapshot());
    }

    public CapturedImage CaptureFullscreen()
    {
        var snapshot = EnumerateSnapshot();
        if (snapshot.Length == 0)
            throw new InvalidOperationException(ScreenPermissionHint("No active displays"));
        var displays = Infos(snapshot);

        var union = MacDisplayGeometry.VirtualDesktopBounds(displays);
        var bitmap = new SKBitmap(union.Width, union.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        var drawn = false;

        foreach (var d in snapshot)
        {
            var image = RequireDisplayImage(d.RawId, d.Info.Index);
            try
            {
                // CG 全局坐标与物理 Bounds 同为左上原点：位图偏移 = display.Bounds - union 原点
                var offsetX = d.Info.Bounds.X - union.X;
                var offsetY = d.Info.Bounds.Y - union.Y;
                DrawImageInto(canvas, image, d.Info.Bounds, offsetX, offsetY);
                drawn = true;
            }
            finally
            {
                MacNative.CFRelease(image);
            }
        }

        if (!drawn) throw new InvalidOperationException(ScreenPermissionHint("No display was captured"));
        _logger.LogDebug("Mac fullscreen capture {W}x{H}", union.Width, union.Height);
        return new CapturedImage(bitmap, union, PrimaryScale(displays), CaptureSource.Fullscreen);
    }

    public CapturedImage CaptureDisplay(int displayIndex)
    {
        var snapshot = EnumerateSnapshot();
        if (displayIndex < 0 || displayIndex >= snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(displayIndex));
        var d = snapshot[displayIndex];

        var image = RequireDisplayImage(d.RawId, displayIndex);
        try
        {
            var bitmap = ImageToBitmap(image, d.Info.Bounds.Width, d.Info.Bounds.Height);
            return new CapturedImage(bitmap, d.Info.Bounds, d.Info.ScaleFactor, CaptureSource.Display);
        }
        finally
        {
            MacNative.CFRelease(image);
        }
    }

    public CapturedImage CaptureRegion(PixelRect region)
    {
        if (region.IsEmpty)
            throw new ArgumentException("Region is empty", nameof(region));

        var snapshot = EnumerateSnapshot();
        var displays = Infos(snapshot);
        var hits = MacDisplayGeometry.IntersectingDisplays(displays, region);
        if (hits.Count == 0)
            throw new ArgumentException($"Region {region} intersects no display", nameof(region));

        var bitmap = new SKBitmap(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        foreach (var d in hits)
        {
            var image = RequireDisplayImage(snapshot[d.Index].RawId, d.Index);
            try
            {
                // 该屏位图相对 region 的偏移（可能是负数：屏在 region 左/上方）
                var offsetX = d.Bounds.X - region.X;
                var offsetY = d.Bounds.Y - region.Y;
                // 只 draw 与 region 的交集，超出部分由 SKCanvas 自动裁剪
                DrawImageInto(canvas, image, d.Bounds, offsetX, offsetY);
            }
            finally
            {
                MacNative.CFRelease(image);
            }
        }

        _logger.LogDebug("Mac region capture {Region} across {N} display(s)", region, hits.Count);
        return new CapturedImage(bitmap, region, PrimaryScale(displays), CaptureSource.Region);
    }

    public CapturedImage CaptureWindow(nint hwnd)
    {
        var windowId = unchecked((uint)hwnd);
        if (windowId == 0)
            throw new ArgumentException("windowId is null", nameof(hwnd));

        if (!_api.TryGetWindowBounds(windowId, out var boundsPt) || boundsPt.Size.Width <= 0)
            throw new InvalidOperationException($"Target window {windowId} has empty rect");

        var displays = EnumerateDisplays();
        var scale = PrimaryScale(displays);

        // BestResolution 直接给物理像素图；尺寸以实际图像为准（DPI 变化时不假设）
        var image = _api.CreateWindowImage(boundsPt, windowId);
        if (image == nint.Zero)
            throw new InvalidOperationException(ScreenPermissionHint($"CGWindowListCreateImage failed for window {windowId}"));
        try
        {
            var actualW = (int)MacNative.CGImageGetWidth(image);
            var actualH = (int)MacNative.CGImageGetHeight(image);
            var bitmap = ImageToBitmap(image, actualW, actualH);

            var boundsPx = new PixelRect(
                (int)Math.Round(boundsPt.Origin.X * scale),
                (int)Math.Round(boundsPt.Origin.Y * scale),
                actualW,
                actualH);
            return new CapturedImage(bitmap, boundsPx, scale, CaptureSource.Window);
        }
        finally
        {
            MacNative.CFRelease(image);
        }
    }

    // ---------- internals ----------

    private nint RequireDisplayImage(uint displayId, int displayIndex)
    {
        var image = _api.CreateDisplayImage(displayId);
        if (image == nint.Zero)
            throw new InvalidOperationException(ScreenPermissionHint($"CGDisplayCreateImage failed for display {displayIndex}"));
        return image;
    }

    private static double PrimaryScale(IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.IsPrimary)?.ScaleFactor ?? displays.FirstOrDefault()?.ScaleFactor ?? 1.0;

    /// <summary>把 CGImage 画到 canvas 的 (offsetX, offsetY)，目标尺寸用物理 Bounds（Retina 2x 图自动缩放对齐）。</summary>
    private static void DrawImageInto(SKCanvas canvas, nint image, PixelRect physicalBounds, int offsetX, int offsetY)
    {
        var imgW = (int)MacNative.CGImageGetWidth(image);
        var imgH = (int)MacNative.CGImageGetHeight(image);
        var info = new SKImageInfo(imgW, imgH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skImage = ImageFromCgPixels(image, info);
        var destRect = new SKRect(offsetX, offsetY, offsetX + physicalBounds.Width, offsetY + physicalBounds.Height);
        canvas.DrawImage(skImage, destRect, new SKPaint { IsAntialias = false });
    }

    /// <summary>CGImage → SKImage：经 CGBitmapContext 统一转 BGRA-premul 行序（像素拷贝，CG 与临时位图即刻可释放）。</summary>
    internal static SKImage ImageFromCgPixels(nint cgImage, SKImageInfo targetInfo)
    {
        // 注意：SKImage.FromBitmap 是引用不拷贝，源位图释放后即变透明——必须 FromPixelCopy
        using var bitmap = ImageToBitmap(cgImage, targetInfo.Width, targetInfo.Height);
        var image = SKImage.FromPixelCopy(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes);
        return image ?? throw new InvalidOperationException("SKImage.FromPixelCopy failed");
    }

    /// <summary>CGImage → SKBitmap（CGBitmapContext BGRA 路径，逐行 Copy 交 CapturedImage 所有）。</summary>
    internal static SKBitmap ImageToBitmap(nint cgImage, int width, int height)
    {
        var colorSpace = MacNative.CGColorSpaceCreateDeviceRGB();
        var ctx = MacNative.CGBitmapContextCreate(nint.Zero, (nuint)width, (nuint)height, 8, (nuint)(width * 4), colorSpace,
            MacNative.SkiaBgraBitmapInfo);
        if (ctx == nint.Zero)
        {
            MacNative.CFRelease(colorSpace);
            throw new InvalidOperationException("CGBitmapContextCreate failed");
        }
        try
        {
            MacNative.CGContextDrawImage(ctx, new CGRect(0, 0, width, height), cgImage);
            var data = MacNative.CGBitmapContextGetData(ctx);
            var stride = (int)MacNative.CGBitmapContextGetBytesPerRow(ctx);
            if (data == nint.Zero) throw new InvalidOperationException("CGBitmapContextGetData failed");

            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            unsafe
            {
                var src = (byte*)data;
                var dst = (byte*)bitmap.GetPixels();
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(src + y * stride, dst + y * width * 4, width * 4L, width * 4L);
                }
            }
            return bitmap;
        }
        finally
        {
            MacNative.CFRelease(ctx);
            MacNative.CFRelease(colorSpace);
        }
    }

    internal static string ScreenPermissionHint(string message) =>
        message +
        " — macOS 屏幕录制权限未授予或显示器枚举为空。请在 系统设置 → 隐私与安全性 → 屏幕录制 中授权 Prtsc 后重试。";
}
