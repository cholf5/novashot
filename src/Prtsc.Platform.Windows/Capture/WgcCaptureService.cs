using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
using Prtsc.Platform.Windows.Interop;
using SkiaSharp;
using Windows.Graphics.Capture;

namespace Prtsc.Platform.Windows.Capture;

/// <summary>
/// WGC (Windows.Graphics.Capture) 主抓帧服务。所有路径优先走 WGC，异常时静默回退到 GDI。
/// M3-4c：CaptureWindow / CaptureDisplay / CaptureFullscreen / CaptureRegion 全部支持 WGC。
///
/// 平台不支持（Win10 &lt;1903 / 无 GraphicsCaptureSession）时构造期一次性判定，之后全部走 GDI。
/// </summary>
public sealed class WgcCaptureService : ICaptureService, IDisposable
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly GdiCaptureService _gdi;
    private readonly ILogger<WgcCaptureService> _logger;
    private readonly IConfigStore _store;
    private readonly bool _wgcSupported;
    private WgcCaptureContext? _ctx;
    private readonly Lock _ctxLock = new();

    public WgcCaptureService(GdiCaptureService gdi, ILogger<WgcCaptureService> logger, IConfigStore store)
    {
        _gdi = gdi;
        _logger = logger;
        _store = store;
        _wgcSupported = ProbeSupported();
        _logger.LogInformation("WGC supported: {Supported}", _wgcSupported);
    }

    public IReadOnlyList<DisplayInfo> EnumerateDisplays() => _gdi.EnumerateDisplays();

    // ==== 主入口 ====

    public CapturedImage CaptureFullscreen()
    {
        if (!_wgcSupported) return _gdi.CaptureFullscreen();
        try
        {
            return CaptureVirtualRect(VirtualDesktop.VirtualScreenBounds(), CaptureSource.Fullscreen);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC CaptureFullscreen failed, falling back to GDI");
            return _gdi.CaptureFullscreen();
        }
    }

    public CapturedImage CaptureDisplay(int displayIndex)
    {
        if (!_wgcSupported) return _gdi.CaptureDisplay(displayIndex);
        try
        {
            var (displays, handles) = VirtualDesktop.EnumerateWithHandles();
            if (displayIndex < 0 || displayIndex >= displays.Count)
                throw new ArgumentOutOfRangeException(nameof(displayIndex));
            var d = displays[displayIndex];
            var bitmap = CaptureMonitor(handles[displayIndex], d.Bounds);
            return new CapturedImage(bitmap, d.Bounds, d.ScaleFactor, CaptureSource.Display);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC CaptureDisplay({Idx}) failed, falling back to GDI", displayIndex);
            return _gdi.CaptureDisplay(displayIndex);
        }
    }

    public CapturedImage CaptureRegion(PixelRect region)
    {
        if (!_wgcSupported) return _gdi.CaptureRegion(region);
        try
        {
            return CaptureVirtualRect(region, CaptureSource.Region);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC CaptureRegion {Region} failed, falling back to GDI", region);
            return _gdi.CaptureRegion(region);
        }
    }

    public CapturedImage CaptureWindow(nint hwnd)
    {
        if (!_wgcSupported) return _gdi.CaptureWindow(hwnd);
        try
        {
            var ctx = EnsureContext();
            var item = CreateItemForWindow(hwnd);
            var grabber = new WgcFrameGrabber(ctx, _logger);
            var bitmap = grabber.CaptureItem(item, FrameTimeout, _store.Current.Capture.IncludeCursor);
            var bounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height);
            return new CapturedImage(bitmap, bounds, PrimaryScale(), CaptureSource.Window);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC CaptureWindow failed for hwnd=0x{Hwnd:X}, falling back to GDI", hwnd);
            return _gdi.CaptureWindow(hwnd);
        }
    }

    // ==== 多屏合成 ====

    private CapturedImage CaptureVirtualRect(PixelRect virtualRect, CaptureSource source)
    {
        if (virtualRect.IsEmpty)
            throw new ArgumentException("Region is empty", nameof(virtualRect));

        var (displays, handles) = VirtualDesktop.EnumerateWithHandles();

        var info = new SKImageInfo(virtualRect.Width, virtualRect.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul);
        var target = new SKBitmap(info);
        // 未被任何显示器覆盖的区域填黑（虚拟桌面在异形显示器组合下可能有洞）
        target.Erase(SKColors.Black);

        try
        {
            using var canvas = new SKCanvas(target);
            for (var i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                var intersect = d.Bounds.Intersect(virtualRect);
                if (intersect.IsEmpty) continue;

                using var monBitmap = CaptureMonitor(handles[i], d.Bounds);
                // src：在显示器位图内的子矩形
                var srcRect = new SKRect(
                    intersect.X - d.Bounds.X,
                    intersect.Y - d.Bounds.Y,
                    intersect.X - d.Bounds.X + intersect.Width,
                    intersect.Y - d.Bounds.Y + intersect.Height);
                // dst：合成 target 内的位置
                var dstRect = new SKRect(
                    intersect.X - virtualRect.X,
                    intersect.Y - virtualRect.Y,
                    intersect.X - virtualRect.X + intersect.Width,
                    intersect.Y - virtualRect.Y + intersect.Height);
                canvas.DrawBitmap(monBitmap, srcRect, dstRect);
            }
            canvas.Flush();
        }
        catch
        {
            target.Dispose();
            throw;
        }

        return new CapturedImage(target, virtualRect, PrimaryScale(), source);
    }

    private SKBitmap CaptureMonitor(nint hmon, PixelRect expectedBounds)
    {
        var ctx = EnsureContext();
        var item = CreateItemForMonitor(hmon);
        var grabber = new WgcFrameGrabber(ctx, _logger);
        var bitmap = grabber.CaptureItem(item, FrameTimeout, _store.Current.Capture.IncludeCursor);

        // 期望显示器为 expectedBounds 尺寸；若不匹配（旋转/热插拔中）按实际返回
        if (bitmap.Width != expectedBounds.Width || bitmap.Height != expectedBounds.Height)
        {
            _logger.LogDebug("WGC monitor bitmap size {W}x{H} != expected {EW}x{EH}",
                bitmap.Width, bitmap.Height, expectedBounds.Width, expectedBounds.Height);
        }
        return bitmap;
    }

    // ==== 基础设施 ====

    private WgcCaptureContext EnsureContext()
    {
        if (_ctx != null) return _ctx;
        lock (_ctxLock)
        {
            _ctx ??= new WgcCaptureContext(_logger);
        }
        return _ctx;
    }

    private static bool ProbeSupported()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint hwnd) =>
        CreateItem(interop => { interop.CreateForWindow(hwnd, in WinRTInterop.IGraphicsCaptureItemIID, out var ptr); return ptr; });

    private static GraphicsCaptureItem CreateItemForMonitor(nint hmon) =>
        CreateItem(interop => { interop.CreateForMonitor(hmon, in WinRTInterop.IGraphicsCaptureItemIID, out var ptr); return ptr; });

    private static GraphicsCaptureItem CreateItem(Func<WinRTInterop.IGraphicsCaptureItemInterop, nint> factoryCall)
    {
        const string classId = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var hr = WinRTInterop.WindowsCreateString(classId, classId.Length, out var hstr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var interopIid = typeof(WinRTInterop.IGraphicsCaptureItemInterop).GUID;
            hr = WinRTInterop.RoGetActivationFactory(hstr, in interopIid, out var factoryPtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                var interop = (WinRTInterop.IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                var itemPtr = factoryCall(interop);
                try
                {
                    var obj = Marshal.GetObjectForIUnknown(itemPtr);
                    return (GraphicsCaptureItem)obj;
                }
                finally
                {
                    Marshal.Release(itemPtr);
                }
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }
        }
        finally
        {
            WinRTInterop.WindowsDeleteString(hstr);
        }
    }

    private static double PrimaryScale() => User32.GetDpiForSystem() / 96.0;

    public void Dispose()
    {
        _ctx?.Dispose();
        _ctx = null;
    }
}
