using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Windowing;

/// <summary>
/// CGWindowListCopyWindowInfo 的可 Fake 门面。
/// </summary>
public interface IWindowListApi
{
    /// <summary>返回全部窗口行（kCGWindowListOptionAll 语义，Z 序）。</summary>
    WinListRow[] CopyWindowList();
}

/// <summary>
/// 行 DTO：CGWindowList 字典里 Novashot 关心的字段。
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
