using Novashot.Core.Common;
using Novashot.Core.Windows;

namespace Novashot.App.Views;

/// <summary>
/// overlay 交互产出（统一交互，docs/plans/2026-11-23-unified-capture-overlay-design.md）：
/// 拖拽出选框确认 = <see cref="Region"/>；悬停吸附原地点击/Enter 确认 = <see cref="WindowCapture"/>
/// （PrintWindow 重捕获语义，完整窗口内容优先，见设计稿像素来源裁决 B）。
/// </summary>
public abstract record OverlayCaptureResult
{
    /// <summary>区域确认：虚拟桌面物理像素 region（跨屏拼接由 ICaptureService.CaptureRegion 完成）。</summary>
    public sealed record Region(PixelRect Rect) : OverlayCaptureResult;

    /// <summary>窗口确认：吸附命中的目标窗口；MainWindow 负责置前后 CaptureWindow(hwnd)。</summary>
    public sealed record WindowCapture(WindowInfo Target) : OverlayCaptureResult;
}
