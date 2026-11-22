using Novashot.Core.Common;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Platform.Linux.Capture;

/// <summary>
/// XRandR 显示器几何 → Core DisplayInfo 的纯换算。
/// X11 全局坐标（左上原点，物理像素）与 Windows 虚拟桌面同构，无需单位换算；
/// DPI 从毫米尺寸推（不可信时默认 1.0）——X11 无统一 per-monitor scale 报告，先保守。
/// </summary>
public static class LinuxDisplayGeometry
{
    public const double DefaultScaleFactor = 1.0;

    /// <summary>毫米尺寸 → 每英寸像素。毫米未知(≤0)或计算异常返回 null。</summary>
    public static double? ScaleFromMillimeters(XDisplayRaw raw)
    {
        if (raw.MmWidth <= 0 || raw.Width <= 0) return null;
        var dpi = raw.Width * 25.4 / raw.MmWidth;
        // 常见缩放档位吸附：96/120/144/168/192（96 的整数倍 ±8 内）
        var ratios = new[] { 1.0, 1.25, 1.5, 1.75, 2.0 };
        foreach (var r in ratios)
        {
            if (Math.Abs(dpi - 96 * r) <= 8) return r;
        }
        return null; // 非标准 DPI → 调用方用默认 1.0
    }

    public static DisplayInfo ToDisplayInfo(XDisplayRaw raw, double scaleFactor, int index) =>
        new(
            index,
            raw.OutputName,
            new PixelRect(raw.X, raw.Y, raw.Width, raw.Height),
            new PixelRect(raw.X, raw.Y, raw.Width, raw.Height), // X11 无 WorkArea 概念，先对齐 Bounds
            scaleFactor,
            raw.IsPrimary);

    /// <summary>索引稳定排序：primary 在前，其余按 (Y, X)（对齐 MacDisplayGeometry.SortForIndexing 语义）。</summary>
    public static XDisplayRaw[] SortForIndexing(IEnumerable<XDisplayRaw> displays)
    {
        var list = displays.ToList();
        list.Sort((a, b) =>
        {
            if (a.IsPrimary != b.IsPrimary) return a.IsPrimary ? -1 : 1;
            var byY = a.Y.CompareTo(b.Y);
            return byY != 0 ? byY : a.X.CompareTo(b.X);
        });
        return [.. list];
    }

    /// <summary>全部显示器的包围盒（支持负坐标，X11 左屏在负区）。</summary>
    public static PixelRect VirtualDesktopBounds(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return default;
        var result = displays[0].Bounds;
        for (var i = 1; i < displays.Count; i++)
            result = PixelRect.Union(result, displays[i].Bounds);
        return result;
    }

    /// <summary>与 region 有交集的显示器（按索引序）。</summary>
    public static List<DisplayInfo> IntersectingDisplays(IReadOnlyList<DisplayInfo> displays, PixelRect region)
    {
        var result = new List<DisplayInfo>();
        foreach (var d in displays)
            if (!d.Bounds.Intersect(region).IsEmpty)
                result.Add(d);
        return result;
    }

    public static double PrimaryScale(IReadOnlyList<DisplayInfo> displays)
    {
        foreach (var d in displays)
            if (d.IsPrimary) return d.ScaleFactor;
        return displays.Count > 0 ? displays[0].ScaleFactor : DefaultScaleFactor;
    }
}
