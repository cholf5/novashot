using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Capture;

/// <summary>
/// CG interop 的可 Fake 门面。MacCaptureService 依赖此接口做纯逻辑测试；
/// 真实现 CGDisplayApi 直接调 MacNative。
/// </summary>
public interface IMacDisplayApi
{
    /// <summary>枚举活跃显示器（CGGetActiveDisplayList + 逐屏 raw 信息）。</summary>
    MacDisplayRaw[] EnumerateDisplays();

    /// <summary>抓取整屏 CGImage（CGDisplayCreateImage）。失败返回 nint.Zero。</summary>
    nint CreateDisplayImage(uint displayId);

    /// <summary>抓取窗口图像（CGWindowListCreateImage，BestResolution）。失败返回 nint.Zero。</summary>
    nint CreateWindowImage(CGRect screenBounds, uint windowId);

    /// <summary>解析窗口 bounds 字典里的点几何；失败返回 CGRect.Null。</summary>
    bool TryGetWindowBounds(uint windowId, out CGRect bounds);
}
