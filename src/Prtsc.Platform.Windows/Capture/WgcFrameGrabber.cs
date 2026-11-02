using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Prtsc.Platform.Windows.Interop;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Prtsc.Platform.Windows.Capture;

/// <summary>
/// 一次性抓帧的 WGC 会话。生命周期：新建 -&gt; <see cref="CaptureItem"/> -&gt; Dispose。
/// FrameArrived 会重复触发；我们只取第一帧就停。
/// </summary>
internal sealed class WgcFrameGrabber
{
    private readonly WgcCaptureContext _ctx;
    private readonly ILogger _logger;

    public WgcFrameGrabber(WgcCaptureContext ctx, ILogger logger)
    {
        _ctx = ctx;
        _logger = logger;
    }

    /// <summary>
    /// 用 GraphicsCaptureItem 抓单帧。超时抛 <see cref="TimeoutException"/>。
    /// 返回 BGRA8888 premul 的 SKBitmap；由调用方持有。
    /// </summary>
    public SKBitmap CaptureItem(GraphicsCaptureItem item, TimeSpan timeout, bool includeCursor)
    {
        var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException($"GraphicsCaptureItem has empty size: {size.Width}x{size.Height}");

        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _ctx.WinRTDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size);

        var session = framePool.CreateCaptureSession(item);
        TrySetBool(session, "IsBorderRequired", false);
        TrySetBool(session, "IsCursorCaptureEnabled", includeCursor);

        using var frameReady = new ManualResetEventSlim(false);
        Direct3D11CaptureFrame? captured = null;

        framePool.FrameArrived += OnFrameArrived;

        void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
        {
            if (captured != null) return;
            var frame = pool.TryGetNextFrame();
            if (frame != null)
            {
                captured = frame;
                frameReady.Set();
            }
        }

        try
        {
            session.StartCapture();
            if (!frameReady.Wait(timeout))
                throw new TimeoutException($"WGC first frame timed out ({timeout.TotalMilliseconds:F0} ms)");
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }

        if (captured == null)
            throw new InvalidOperationException("WGC frame null after signal");

        try
        {
            return FrameToSkBitmap(captured, size.Width, size.Height);
        }
        finally
        {
            captured.Dispose();
            session.Dispose();
            framePool.Dispose();
        }
    }

    private SKBitmap FrameToSkBitmap(Direct3D11CaptureFrame frame, int width, int height)
    {
        // IDirect3DSurface -> ID3D11Texture2D
        var surface = frame.Surface;
        var access = (WinRTInterop.IDirect3DDxgiInterfaceAccess)(object)surface;
        var iid = WinRTInterop.ID3D11Texture2DIID;
        var hr = access.GetInterface(in iid, out var texPtr);
        Marshal.ThrowExceptionForHR(hr);

        using var texture = new ID3D11Texture2D(texPtr);

        // Staging 拷贝
        var desc = texture.Description;
        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using var staging = _ctx.Device.CreateTexture2D(stagingDesc);
        _ctx.ImmediateContext.CopyResource(staging, texture);

        var map = _ctx.ImmediateContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            unsafe
            {
                var dstPitch = width * 4;
                var srcBase = (byte*)map.DataPointer;
                var dstBase = (byte*)bitmap.GetPixels();
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        srcBase + (long)y * map.RowPitch,
                        dstBase + (long)y * dstPitch,
                        dstPitch, dstPitch);
                }
            }
            bitmap.NotifyPixelsChanged();
            return bitmap;
        }
        finally
        {
            _ctx.ImmediateContext.Unmap(staging, 0);
            surface.Dispose();
        }
    }

    private static void TrySetBool(GraphicsCaptureSession session, string propertyName, bool value)
    {
        try
        {
            var prop = typeof(GraphicsCaptureSession).GetProperty(propertyName);
            if (prop != null && prop.CanWrite) prop.SetValue(session, value);
        }
        catch
        {
            // 老平台上没有该属性，忽略
        }
    }
}
