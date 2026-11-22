using Novashot.Core.Common;

namespace Novashot.Core.Windows;

/// <summary>
/// 窗口吸附命中测试（纯逻辑）：指针物理坐标落在哪个窗口。
/// 输入必须按 Z 序排列（前台在前，<see cref="IWindowService.EnumerateWindows"/> 的契约），
/// 返回第一个 <see cref="WindowInfo.Bounds"/> 包含指针的窗口；无命中返回 null。
/// 最小化/不可见窗口由枚举层过滤，这里不做二次过滤。
/// </summary>
public static class SnapHitTester
{
    public static WindowInfo? Hit(IReadOnlyList<WindowInfo> windowsZOrder, int physicalX, int physicalY)
    {
        foreach (var w in windowsZOrder)
        {
            if (w.Bounds.Contains(physicalX, physicalY)) return w;
        }
        return null;
    }
}
