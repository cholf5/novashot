using Microsoft.Extensions.Logging;
using SkiaSharp;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Platform.Linux.Interop;

namespace Prtsc.Platform.Linux.Capture;

/// <summary>
/// X11 抓帧实现（语义对齐 MacCaptureService：同步、整虚拟桌面、物理像素）。
/// XGetImage 从根窗抓屏——所有窗口的合成结果已在根窗上（合成 WM）或直接可见（非合成 WM）。
/// 每次 <see cref="EnumerateDisplays"/> 返回新数组（MAC-1 坑 #3 教训：Region 流程会重入枚举）。
/// </summary>
public sealed class LinuxCaptureService : ICaptureService
{
    private readonly IXLibApi _api;
    private readonly ILogger<LinuxCaptureService> _logger;
    private readonly List<nint> _crtcs = [];

    public LinuxCaptureService(IXLibApi api, ILogger<LinuxCaptureService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public IReadOnlyList<DisplayInfo> EnumerateDisplays()
    {
        var raws = _api.EnumerateDisplays();
        var sorted = LinuxDisplayGeometry.SortForIndexing(raws);
        // 每次返回新数组：调用方会在枚举返回值的过程中再次触发本方法（Region 流程逐屏 CaptureDisplay）
        var displays = new DisplayInfo[sorted.Length];
        _crtcs.Clear();
        for (var i = 0; i < sorted.Length; i++)
        {
            var scale = LinuxDisplayGeometry.ScaleFromMillimeters(sorted[i]) ?? LinuxDisplayGeometry.DefaultScaleFactor;
            displays[i] = LinuxDisplayGeometry.ToDisplayInfo(sorted[i], scale, i);
            _crtcs.Add(sorted[i].Crtc);
        }
        return displays;
    }

    public CapturedImage CaptureFullscreen()
    {
        var displays = EnumerateDisplays();
        if (displays.Count == 0)
            throw new InvalidOperationException(NoDisplayHint("No active displays"));

        var union = LinuxDisplayGeometry.VirtualDesktopBounds(displays);
        var bitmap = GrabRootRegion(union);
        _logger.LogDebug("Linux fullscreen capture {W}x{H}", union.Width, union.Height);
        return new CapturedImage(bitmap, union, LinuxDisplayGeometry.PrimaryScale(displays), CaptureSource.Fullscreen);
    }

    public CapturedImage CaptureDisplay(int displayIndex)
    {
        var displays = EnumerateDisplays();
        if (displayIndex < 0 || displayIndex >= displays.Count)
            throw new ArgumentOutOfRangeException(nameof(displayIndex));
        var d = displays[displayIndex];

        var bitmap = GrabRootRegion(d.Bounds);
        return new CapturedImage(bitmap, d.Bounds, d.ScaleFactor, CaptureSource.Display);
    }

    public CapturedImage CaptureRegion(PixelRect region)
    {
        if (region.IsEmpty)
            throw new ArgumentException("Region is empty", nameof(region));

        var displays = EnumerateDisplays();
        var hits = LinuxDisplayGeometry.IntersectingDisplays(displays, region);
        if (hits.Count == 0)
            throw new ArgumentException($"Region {region} intersects no display", nameof(region));

        var bitmap = GrabRootRegion(region);
        _logger.LogDebug("Linux region capture {Region} across {N} display(s)", region, hits.Count);
        return new CapturedImage(bitmap, region, LinuxDisplayGeometry.PrimaryScale(displays), CaptureSource.Region);
    }

    public CapturedImage CaptureWindow(nint hwnd)
    {
        if (hwnd == nint.Zero)
            throw new ArgumentException("Window handle is null", nameof(hwnd));

        var info = _api.QueryWindow(hwnd)
            ?? throw new InvalidOperationException($"Window 0x{hwnd:X} is gone (XGetWindowAttributes failed)");

        // 窗口图像优先 XComposite NameWindowPixmap（含被遮挡内容）；无合成器回退根窗矩形拷贝
        var image = _api.GetWindowImage(hwnd, info.Width, info.Height)
            ?? _api.GetImage(info.X, info.Y, info.Width, info.Height);
        if (image is null)
            throw new InvalidOperationException($"Failed to grab window 0x{hwnd:X} ({info.WmClassName})");

        try
        {
            var bitmap = ImageToBitmap(image, info.Width, info.Height);
            return new CapturedImage(bitmap, new PixelRect(info.X, info.Y, info.Width, info.Height),
                LinuxDisplayGeometry.DefaultScaleFactor, CaptureSource.Window);
        }
        finally
        {
            image.Dispose();
        }
    }

    // ---- helpers ----

    /// <summary>从根窗抓任意物理像素矩形（含跨屏；X11 根窗即虚拟桌面）。</summary>
    private SKBitmap GrabRootRegion(PixelRect region)
    {
        if (region.IsEmpty)
            throw new InvalidOperationException(NoDisplayHint("Empty capture region"));

        var image = _api.GetImage(region.X, region.Y, region.Width, region.Height)
            ?? throw new InvalidOperationException(NoDisplayHint(
                $"XGetImage failed for {region} (screen depth/format unsupported?)"));
        try
        {
            return ImageToBitmap(image, region.Width, region.Height);
        }
        finally
        {
            image.Dispose();
        }
    }

    internal static SKBitmap ImageToBitmap(IXImage image, int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var addr = bitmap.GetPixels();
        unsafe
        {
            var dst = (uint*)addr;
            for (var y = 0; y < height; y++)
            {
                var row = dst + y * width;
                for (var x = 0; x < width; x++)
                    row[x] = image.GetPixel(x, y);
            }
        }
        return bitmap;
    }

    private static string NoDisplayHint(string message) =>
        $"{message}. Check DISPLAY is set and X server is reachable (Wayland sessions: XWayland only for now).";
}
