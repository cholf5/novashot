using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Platform.Windows.Interop;
using SkiaSharp;

namespace Prtsc.Platform.Windows.Capture;

/// <summary>
/// GDI BitBlt 抓帧。作为默认路径，兼容性最好；Win10 1903+ 上未来将由 WGC 替换。
/// 所有几何以物理像素为单位（依赖 PerMonitorV2 DPI 感知）。
/// </summary>
public sealed class GdiCaptureService : ICaptureService
{
    private readonly ILogger<GdiCaptureService> _logger;

    public GdiCaptureService(ILogger<GdiCaptureService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<DisplayInfo> EnumerateDisplays() => VirtualDesktop.Enumerate();

    public CapturedImage CaptureFullscreen()
    {
        var bounds = VirtualDesktop.VirtualScreenBounds();
        return CaptureRegion(bounds, CaptureSource.Fullscreen);
    }

    public CapturedImage CaptureDisplay(int displayIndex)
    {
        var displays = EnumerateDisplays();
        if (displayIndex < 0 || displayIndex >= displays.Count)
            throw new ArgumentOutOfRangeException(nameof(displayIndex));
        var d = displays[displayIndex];
        var img = CaptureRegion(d.Bounds, CaptureSource.Display);
        return new CapturedImage(img.Bitmap, d.Bounds, d.ScaleFactor, CaptureSource.Display);
    }

    public CapturedImage CaptureRegion(PixelRect region) => CaptureRegion(region, CaptureSource.Region);

    public CapturedImage CaptureWindow(nint hwnd)
    {
        if (hwnd == nint.Zero) throw new ArgumentException("hwnd is null", nameof(hwnd));

        // 真实几何（去 DWM 阴影）→ 屏幕坐标；PrintWindow 走窗口自己的 client+chrome
        var bounds = GetWindowBoundsForCapture(hwnd);
        if (bounds.IsEmpty)
            throw new InvalidOperationException("Target window has empty rect");

        _logger.LogDebug("PrintWindow hwnd=0x{Hwnd:X} bounds={Bounds}", hwnd, bounds);

        var screenDc = User32.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            throw new InvalidOperationException("GetDC failed");

        var memDc = nint.Zero;
        var hbm = nint.Zero;
        var oldObj = nint.Zero;
        try
        {
            memDc = Gdi32.CreateCompatibleDC(screenDc);
            hbm = Gdi32.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            oldObj = Gdi32.SelectObject(memDc, hbm);

            var ok = User32.PrintWindow(hwnd, memDc, User32.PW_RENDERFULLCONTENT);
            if (!ok)
            {
                // 回退：BitBlt 该窗口在屏幕上的可见部分
                _logger.LogWarning("PrintWindow failed for hwnd=0x{Hwnd:X}, fall back to BitBlt", hwnd);
                if (!Gdi32.BitBlt(memDc, 0, 0, bounds.Width, bounds.Height,
                                  screenDc, bounds.X, bounds.Y, Gdi32.SRCCOPY | Gdi32.CAPTUREBLT))
                {
                    throw new InvalidOperationException(
                        $"CaptureWindow fallback BitBlt failed, HRESULT={Marshal.GetHRForLastWin32Error():X8}");
                }
            }

            var bitmap = HBitmapToSkBitmap(hbm, bounds.Width, bounds.Height, screenDc);
            return new CapturedImage(bitmap, bounds, PrimaryScale(), CaptureSource.Window);
        }
        finally
        {
            if (oldObj != nint.Zero) Gdi32.SelectObject(memDc, oldObj);
            if (hbm != nint.Zero) Gdi32.DeleteObject(hbm);
            if (memDc != nint.Zero) Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static PixelRect GetWindowBoundsForCapture(nint hwnd)
    {
        var hr = Interop.DwmApi.DwmGetWindowAttribute(
            hwnd, Interop.DwmApi.DWMWA_EXTENDED_FRAME_BOUNDS,
            out User32.RECT rc, Marshal.SizeOf<User32.RECT>());
        if (hr != 0)
        {
            if (!User32.GetWindowRect(hwnd, out rc)) return default;
        }
        return new PixelRect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    private CapturedImage CaptureRegion(PixelRect region, CaptureSource source)
    {
        if (region.IsEmpty)
            throw new ArgumentException("Region is empty", nameof(region));

        _logger.LogDebug("GDI capture region {Region}", region);

        var screenDc = User32.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            throw new InvalidOperationException("GetDC failed");

        var memDc = nint.Zero;
        var hbm = nint.Zero;
        var oldObj = nint.Zero;
        try
        {
            memDc = Gdi32.CreateCompatibleDC(screenDc);
            hbm = Gdi32.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            oldObj = Gdi32.SelectObject(memDc, hbm);

            if (!Gdi32.BitBlt(memDc, 0, 0, region.Width, region.Height,
                              screenDc, region.X, region.Y, Gdi32.SRCCOPY | Gdi32.CAPTUREBLT))
            {
                throw new InvalidOperationException($"BitBlt failed, HRESULT={Marshal.GetHRForLastWin32Error():X8}");
            }

            var bitmap = HBitmapToSkBitmap(hbm, region.Width, region.Height, screenDc);
            var primaryScale = PrimaryScale();
            return new CapturedImage(bitmap, region, primaryScale, source);
        }
        finally
        {
            if (oldObj != nint.Zero) Gdi32.SelectObject(memDc, oldObj);
            if (hbm != nint.Zero) Gdi32.DeleteObject(hbm);
            if (memDc != nint.Zero) Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static double PrimaryScale()
    {
        return User32.GetDpiForSystem() / 96.0;
    }

    private static SKBitmap HBitmapToSkBitmap(nint hbm, int width, int height, nint screenDc)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        var bmi = new Gdi32.BITMAPINFO
        {
            bmiHeader = new Gdi32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
                biSizeImage = 0
            },
            bmiColors = new uint[3]
        };

        var pixels = bitmap.GetPixels(out _);
        var scanned = Gdi32.GetDIBits(screenDc, hbm, 0, (uint)height, pixels, ref bmi, Gdi32.DIB_RGB_COLORS);
        if (scanned == 0)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("GetDIBits returned 0");
        }

        // GDI 32bpp 无 alpha 语义；把 alpha 全设为 255 以确保下游 Premul 正常。
        unsafe
        {
            var span = new Span<byte>((void*)pixels, width * height * 4);
            for (var i = 3; i < span.Length; i += 4)
                span[i] = 0xFF;
        }

        bitmap.NotifyPixelsChanged();
        return bitmap;
    }
}
