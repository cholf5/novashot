using Novashot.Core.Common;
using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Capture;

/// <summary>CGDisplay 原始信息（点单位几何），由 interop 层产出、可 Fake。</summary>
/// <param name="Id">CGDirectDisplayID。</param>
/// <param name="WidthPx">物理像素宽（CGDisplayPixelsWide）。</param>
/// <param name="HeightPx">物理像素高。</param>
/// <param name="GlobalRect">CGDisplayBounds：全局坐标点几何。</param>
/// <param name="IsMain">主屏。</param>
public sealed record MacDisplayRaw(
    uint Id,
    uint WidthPx,
    uint HeightPx,
    CGRect GlobalRect,
    bool IsMain);

/// <summary>
/// CG 显示器几何 → Core DisplayInfo 的纯换算。
/// CG 全局坐标（左上原点，点）与 Windows 虚拟桌面（左上原点，物理像素）语义同构，
/// 差异仅在单位：物理像素 = 点 × scaleFactor（Retina scaleFactor = WidthPx / GlobalRect.Width）。
/// </summary>
public static class MacDisplayGeometry
{
    public static Core.Common.DisplayInfo ToDisplayInfo(MacDisplayRaw raw, double scaleFactor, int index)
    {
        var x = (int)Math.Round(raw.GlobalRect.Origin.X * scaleFactor);
        var y = (int)Math.Round(raw.GlobalRect.Origin.Y * scaleFactor);
        var w = (int)Math.Round(raw.GlobalRect.Size.Width * scaleFactor);
        var h = (int)Math.Round(raw.GlobalRect.Size.Height * scaleFactor);
        return new DisplayInfo(
            index,
            $"CGDisplay{raw.Id}",
            new PixelRect(x, y, w, h),
            new PixelRect(x, y, w, h), // macOS 无独立 WorkArea 概念（菜单栏由 overlay 自行留白），先对齐 Bounds
            scaleFactor,
            raw.IsMain);
    }

    /// <summary>由 raw 数据推导 scaleFactor（Retina=2.0、普通=1.0）；几何异常时回退 1.0。</summary>
    public static double DeriveScaleFactor(MacDisplayRaw raw) =>
        raw.GlobalRect.Size.Width > 0 ? (double)raw.WidthPx / raw.GlobalRect.Size.Width : 1.0;

    /// <summary>排序：y 优先（上到下），再 x（左到右）。与 Windows VirtualDesktop.Enumerate 语义对齐。</summary>
    public static MacDisplayRaw[] SortForIndexing(MacDisplayRaw[] raws)
    {
        var sorted = (MacDisplayRaw[])raws.Clone();
        Array.Sort(sorted, (a, b) =>
        {
            var byY = a.GlobalRect.Origin.Y.CompareTo(b.GlobalRect.Origin.Y);
            if (byY != 0) return byY;
            return a.GlobalRect.Origin.X.CompareTo(b.GlobalRect.Origin.X);
        });
        return sorted;
    }

    /// <summary>虚拟桌面 = 全部显示器 Bounds 的联集（物理像素）。</summary>
    public static PixelRect VirtualDesktopBounds(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return default;

        var minX = displays.Min(d => d.Bounds.X);
        var minY = displays.Min(d => d.Bounds.Y);
        var maxX = displays.Max(d => d.Bounds.X + d.Bounds.Width);
        var maxY = displays.Max(d => d.Bounds.Y + d.Bounds.Height);
        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// 找出与物理像素 region 相交的显示器，按 Index 排序。
    /// 跨屏 region 由 <see cref="MacCaptureService"/> 逐屏分解抓取。
    /// </summary>
    public static List<DisplayInfo> IntersectingDisplays(IReadOnlyList<DisplayInfo> displays, PixelRect region)
    {
        var result = new List<DisplayInfo>();
        foreach (var d in displays)
        {
            if (!d.Bounds.IsEmpty && !region.IsEmpty && !d.Bounds.Intersect(region).IsEmpty)
                result.Add(d);
        }
        result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }
}
