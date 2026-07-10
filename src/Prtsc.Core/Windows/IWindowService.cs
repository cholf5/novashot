namespace Prtsc.Core.Windows;

/// <summary>
/// 窗口枚举与前台窗口获取。平台无关；Windows 端由 <c>WindowEnumerator</c> 提供实现。
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// 列出所有"用户可见、可截屏"的顶层窗口。过滤规则由实现决定：
    /// 至少排除 Prtsc 自身窗口、不可见窗口、DWM cloaked 窗口、面积过小的窗口。
    /// 顺序按 Z-order（前台在前）。
    /// </summary>
    IReadOnlyList<WindowInfo> EnumerateWindows();

    /// <summary>
    /// 当前前台窗口。若无可用返回 null。已排除 Prtsc 自身。
    /// </summary>
    WindowInfo? GetForegroundWindow();

    /// <summary>
    /// 尝试把指定 HWND 恢复并置为前台。失败静默。
    /// 用于窗口截图前把目标窗口浮到最上，让 PrintWindow(PW_RENDERFULLCONTENT) 拿到完整合成结果。
    /// </summary>
    void TryBringToForeground(nint hwnd);
}
