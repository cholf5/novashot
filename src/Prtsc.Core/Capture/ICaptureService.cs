using Prtsc.Core.Common;

namespace Prtsc.Core.Capture;

/// <summary>
/// 平台无关的截屏能力。由 Prtsc.Platform.Windows 提供实现。
/// </summary>
public interface ICaptureService
{
    /// <summary>
    /// 枚举当前所有显示器。物理像素坐标。
    /// </summary>
    IReadOnlyList<DisplayInfo> EnumerateDisplays();

    /// <summary>
    /// 抓取整个虚拟桌面（跨显示器合成）。
    /// </summary>
    CapturedImage CaptureFullscreen();

    /// <summary>
    /// 抓取指定显示器。
    /// </summary>
    CapturedImage CaptureDisplay(int displayIndex);

    /// <summary>
    /// 抓取虚拟桌面上的物理像素矩形。
    /// </summary>
    CapturedImage CaptureRegion(PixelRect region);

    /// <summary>
    /// 抓取指定顶层窗口（含被遮挡区域），基于 PrintWindow(PW_RENDERFULLCONTENT)。
    /// hwnd 无效或该窗口拒绝 PrintWindow 时会退化为屏幕 BitBlt 该窗口矩形。
    /// </summary>
    CapturedImage CaptureWindow(nint hwnd);
}
