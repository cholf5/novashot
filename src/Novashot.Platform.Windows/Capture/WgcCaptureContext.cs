using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Novashot.Platform.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.DirectX.Direct3D11;

namespace Novashot.Platform.Windows.Capture;

/// <summary>
/// 承载一份进程级共享的 D3D11 设备与其对应 WinRT <see cref="IDirect3DDevice"/> 包装。
/// WGC 的 <c>Direct3D11CaptureFramePool.CreateFreeThreaded</c> 需要 <see cref="IDirect3DDevice"/>；
/// 我们通过 Vortice.Direct3D11 建 device，再走 <c>CreateDirect3D11DeviceFromDXGIDevice</c> 桥接到 WinRT。
///
/// 单例：一个进程一个 device 即可，GDI 抓帧不会用到这里。
/// </summary>
internal sealed class WgcCaptureContext : IDisposable
{
    private readonly ILogger _logger;
    private bool _disposed;

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext ImmediateContext { get; }
    public IDirect3DDevice WinRTDevice { get; }

    public WgcCaptureContext(ILogger logger)
    {
        _logger = logger;

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        };

        var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        // Debug 层若未安装 (Windows SDK 组件) 也无所谓，创建时会自动跳过
#endif

        var hr = D3D11.D3D11CreateDevice(
            adapter: null,
            DriverType.Hardware,
            flags,
            featureLevels,
            out var device,
            out _,
            out var context);

        if (hr.Failure || device is null)
        {
            // 硬件失败退回 WARP（软件光栅），供无 GPU/远程桌面场景
            _logger.LogWarning("D3D11 hardware device creation failed (hr={Hr}), trying WARP", hr);
            hr = D3D11.D3D11CreateDevice(
                adapter: null,
                DriverType.Warp,
                flags,
                featureLevels,
                out device,
                out _,
                out context);
            hr.CheckError();
        }

        Device = device!;
        ImmediateContext = context!;

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        WinRTInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
        try
        {
            // 必须用 CsWinRT 运行时的 MarshalInterface（产生真正的投影包装）；手写
            // GetObjectForIUnknown 拿到的是 RCW，后续 CreateFreeThreaded 的 ABI 编组
            // 无法为 RCW 建 CCW（InvalidCastException: Failed to create a CCW）。
            // FromAbi 会 AddRef，finally 里 Release 原指针平衡。
            WinRTDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { WinRTDevice?.Dispose(); } catch { }
        try { ImmediateContext?.Dispose(); } catch { }
        try { Device?.Dispose(); } catch { }
    }
}
