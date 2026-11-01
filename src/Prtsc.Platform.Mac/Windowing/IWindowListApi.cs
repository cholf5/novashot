using Prtsc.Platform.Mac.Interop;

namespace Prtsc.Platform.Mac.Windowing;

/// <summary>
/// CGWindowListCopyWindowInfo 的可 Fake 门面。
/// </summary>
public interface IWindowListApi
{
    /// <summary>返回全部窗口行（kCGWindowListOptionAll 语义，Z 序）。</summary>
    WinListRow[] CopyWindowList();
}

/// <summary>
/// 行 DTO：CGWindowList 字典里 Prtsc 关心的字段。
/// </summary>
public sealed record WinListRow(
    uint WindowId,
    string Owner,
    string Title,
    CGRect Bounds,
    int Layer,
    double Alpha,
    bool IsOnscreen,
    int OwnerPid);
