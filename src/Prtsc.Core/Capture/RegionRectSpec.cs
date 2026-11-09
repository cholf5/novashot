using Prtsc.Core.Common;

namespace Prtsc.Core.Capture;

/// <summary>G-26 Capture Launcher：区域字段解析错误类型。</summary>
public enum RegionRectErrorKind
{
    None,
    /// <summary>某个字段不是整数（含空串/null）。</summary>
    NotANumber,
    /// <summary>宽或高 ≤ 0。</summary>
    NonPositiveSize,
}

/// <summary>
/// G-26 Capture Launcher 的区域输入纯逻辑：X/Y/W/H 文本 → <see cref="PixelRect"/>，
/// 以及钳进虚拟桌面边界。物理像素、虚拟桌面坐标（X/Y 可为负——多屏布局）。
/// </summary>
public static class RegionRectSpec
{
    /// <summary>
    /// 解析四个整数字段。任一字段非整数（含 null/空串/小数）→ <see cref="RegionRectErrorKind.NotANumber"/>；
    /// W/H ≤ 0 → <see cref="RegionRectErrorKind.NonPositiveSize"/>。首尾空白容忍。
    /// </summary>
    public static bool TryParse(string? x, string? y, string? w, string? h,
        out PixelRect rect, out RegionRectErrorKind error)
    {
        rect = default;
        error = RegionRectErrorKind.None;

        var fields = new[] { (x, "X"), (y, "Y"), (w, "W"), (h, "H") };
        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(fields[i].Item1?.Trim(), out values[i]))
            {
                error = RegionRectErrorKind.NotANumber;
                return false;
            }
        }

        if (values[2] <= 0 || values[3] <= 0)
        {
            error = RegionRectErrorKind.NonPositiveSize;
            return false;
        }

        rect = new PixelRect(values[0], values[1], values[2], values[3]);
        return true;
    }

    /// <summary>
    /// 把 rect 钳进 bounds（虚拟桌面）：先按边界截宽高，再位移 X/Y 入界（保持尺寸优先）。
    /// rect 为空 → default；bounds 为空（无显示器）→ 原样返回。
    /// </summary>
    public static PixelRect ClampToBounds(PixelRect rect, PixelRect bounds)
    {
        if (rect.IsEmpty) return default;
        if (bounds.IsEmpty) return rect;

        var width = Math.Min(rect.Width, bounds.Width);
        var height = Math.Min(rect.Height, bounds.Height);
        var x = Math.Clamp(rect.X, bounds.X, bounds.Right - width);
        var y = Math.Clamp(rect.Y, bounds.Y, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>显示器集合 → 虚拟桌面 bounds（并集）；空集合返回 default。</summary>
    public static PixelRect VirtualDesktopBounds(IReadOnlyList<DisplayInfo> displays)
    {
        var bounds = default(PixelRect);
        foreach (var d in displays)
            bounds = PixelRect.Union(bounds, d.Bounds);
        return bounds;
    }
}
