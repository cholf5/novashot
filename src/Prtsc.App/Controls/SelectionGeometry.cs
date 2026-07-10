using System;

namespace Prtsc.App.Controls;

/// <summary>
/// SelectionAdorner 交互中的纯几何计算，独立成 static helper 便于单元测试。
/// 坐标全为 DIP，Rect 语义与 Avalonia 一致。
/// </summary>
public static class SelectionGeometry
{
    /// <summary>Grip 类型 —— 4 角、4 边中点、内部、外部。</summary>
    public enum Grip { NW, N, NE, E, SE, S, SW, W, Inside, Outside }

    /// <summary>由两个对角点构造非负矩形（自动 min/max）。</summary>
    public static Avalonia.Rect FromCorners(double ax, double ay, double bx, double by)
    {
        var x = Math.Min(ax, bx);
        var y = Math.Min(ay, by);
        var w = Math.Abs(ax - bx);
        var h = Math.Abs(ay - by);
        return new Avalonia.Rect(x, y, w, h);
    }

    /// <summary>把 rect 裁进 [0, w] x [0, h]，并归一化（负宽/高翻转）。</summary>
    public static Avalonia.Rect ClampAndNormalize(Avalonia.Rect r, double w, double h)
    {
        var x1 = Math.Clamp(r.X, 0, w);
        var y1 = Math.Clamp(r.Y, 0, h);
        var x2 = Math.Clamp(r.Right, 0, w);
        var y2 = Math.Clamp(r.Bottom, 0, h);
        if (x2 < x1) (x1, x2) = (x2, x1);
        if (y2 < y1) (y1, y2) = (y2, y1);
        return new Avalonia.Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// 以 <paramref name="start"/> 的宽高比重写 <paramref name="r"/>，保持 grip 对角锚点不动。
    /// 仅对 4 角有效，边中 handle 直接返回 r。
    /// </summary>
    public static Avalonia.Rect ApplyAspectLock(Avalonia.Rect r, Avalonia.Rect start, Grip grip)
    {
        if (grip is not (Grip.NW or Grip.NE or Grip.SW or Grip.SE)) return r;
        if (start.Width <= 0 || start.Height <= 0) return r;

        var aspect = start.Width / start.Height;
        double newW = r.Width;
        double newH = r.Height;
        if (newW / aspect > newH) newH = newW / aspect;
        else newW = newH * aspect;

        double x = r.X, y = r.Y;
        switch (grip)
        {
            case Grip.NW: x = start.Right - newW; y = start.Bottom - newH; break;
            case Grip.NE: x = start.X;             y = start.Bottom - newH; break;
            case Grip.SW: x = start.Right - newW; y = start.Y; break;
            case Grip.SE: x = start.X;             y = start.Y; break;
        }
        return new Avalonia.Rect(x, y, newW, newH);
    }

    /// <summary>以 <paramref name="start"/> 中心对称：所摁 handle 的位移在对侧同幅反向发生。</summary>
    public static Avalonia.Rect ApplyCenterSymmetric(Avalonia.Rect r, Avalonia.Rect start, Grip grip)
    {
        var cx = start.X + start.Width / 2.0;
        var cy = start.Y + start.Height / 2.0;
        var halfW = start.Width / 2.0;
        var halfH = start.Height / 2.0;

        switch (grip)
        {
            case Grip.NW:
            case Grip.NE:
            case Grip.SW:
            case Grip.SE:
                halfW = Math.Max(Math.Abs(r.X - cx), Math.Abs(r.Right - cx));
                halfH = Math.Max(Math.Abs(r.Y - cy), Math.Abs(r.Bottom - cy));
                break;
            case Grip.N:
            case Grip.S:
                halfH = Math.Max(Math.Abs(r.Y - cy), Math.Abs(r.Bottom - cy));
                break;
            case Grip.W:
            case Grip.E:
                halfW = Math.Max(Math.Abs(r.X - cx), Math.Abs(r.Right - cx));
                break;
        }
        return new Avalonia.Rect(cx - halfW, cy - halfH, halfW * 2, halfH * 2);
    }

    /// <summary>
    /// 基于 grip 语义把 (dx, dy) 应用到 <paramref name="start"/>，返回未 clamp 的新 rect。
    /// Grip.Inside 视作平移；Grip.Outside 保持不变。
    /// </summary>
    public static Avalonia.Rect ApplyDrag(Avalonia.Rect start, double dx, double dy, Grip grip) => grip switch
    {
        Grip.Inside  => new Avalonia.Rect(start.X + dx, start.Y + dy, start.Width, start.Height),
        Grip.NW      => FromCorners(start.Right, start.Bottom, start.X + dx, start.Y + dy),
        Grip.NE      => FromCorners(start.X, start.Bottom, start.Right + dx, start.Y + dy),
        Grip.SW      => FromCorners(start.Right, start.Y, start.X + dx, start.Bottom + dy),
        Grip.SE      => FromCorners(start.X, start.Y, start.Right + dx, start.Bottom + dy),
        Grip.N       => FromCorners(start.X, start.Bottom, start.Right, start.Y + dy),
        Grip.S       => FromCorners(start.X, start.Y, start.Right, start.Bottom + dy),
        Grip.W       => FromCorners(start.Right, start.Y, start.X + dx, start.Bottom),
        Grip.E       => FromCorners(start.X, start.Y, start.Right + dx, start.Bottom),
        _            => start,
    };

    /// <summary>
    /// 命中测试。参数：pad = handle 命中盒半边长，slop = 边线拖动的允许偏离距离。
    /// </summary>
    public static Grip HitTest(Avalonia.Rect? selection, Avalonia.Point p, double pad, double slop)
    {
        if (selection is not { } sel) return Grip.Outside;

        if (Near(p, new Avalonia.Point(sel.X, sel.Y), pad)) return Grip.NW;
        if (Near(p, new Avalonia.Point(sel.Right, sel.Y), pad)) return Grip.NE;
        if (Near(p, new Avalonia.Point(sel.Right, sel.Bottom), pad)) return Grip.SE;
        if (Near(p, new Avalonia.Point(sel.X, sel.Bottom), pad)) return Grip.SW;

        if (Near(p, new Avalonia.Point(sel.X + sel.Width / 2, sel.Y), pad)) return Grip.N;
        if (Near(p, new Avalonia.Point(sel.Right, sel.Y + sel.Height / 2), pad)) return Grip.E;
        if (Near(p, new Avalonia.Point(sel.X + sel.Width / 2, sel.Bottom), pad)) return Grip.S;
        if (Near(p, new Avalonia.Point(sel.X, sel.Y + sel.Height / 2), pad)) return Grip.W;

        if (Math.Abs(p.Y - sel.Y) <= slop && p.X >= sel.X && p.X <= sel.Right) return Grip.N;
        if (Math.Abs(p.Y - sel.Bottom) <= slop && p.X >= sel.X && p.X <= sel.Right) return Grip.S;
        if (Math.Abs(p.X - sel.X) <= slop && p.Y >= sel.Y && p.Y <= sel.Bottom) return Grip.W;
        if (Math.Abs(p.X - sel.Right) <= slop && p.Y >= sel.Y && p.Y <= sel.Bottom) return Grip.E;

        if (sel.Contains(p)) return Grip.Inside;
        return Grip.Outside;
    }

    private static bool Near(Avalonia.Point a, Avalonia.Point b, double pad)
        => Math.Abs(a.X - b.X) <= pad && Math.Abs(a.Y - b.Y) <= pad;

    /// <summary>
    /// Flameshot ButtonHandler 简化版：给悬浮工具环挑一个"贴着选框但不越界"的位置。
    /// 优先顺序：下方 → 右侧 → 上方 → 左侧 → 选框内右下角 fallback。
    /// </summary>
    /// <param name="sel">选框（DIP）。</param>
    /// <param name="ring">工具环尺寸。</param>
    /// <param name="overlay">overlay 可用空间（DIP）。</param>
    /// <param name="gap">与选框边的间距。</param>
    /// <returns>ring 左上角坐标（DIP）。</returns>
    public static Avalonia.Point ComputeToolRingPosition(
        Avalonia.Rect sel, Avalonia.Size ring, Avalonia.Size overlay, double gap = 8)
    {
        double ClampX(double x) => Math.Clamp(x, 0, Math.Max(0, overlay.Width - ring.Width));
        double ClampY(double y) => Math.Clamp(y, 0, Math.Max(0, overlay.Height - ring.Height));

        // 1. 下方
        if (sel.Bottom + gap + ring.Height <= overlay.Height)
            return new Avalonia.Point(ClampX(sel.X), sel.Bottom + gap);

        // 2. 右侧
        if (sel.Right + gap + ring.Width <= overlay.Width)
            return new Avalonia.Point(sel.Right + gap, ClampY(sel.Y));

        // 3. 上方
        if (sel.Y - gap - ring.Height >= 0)
            return new Avalonia.Point(ClampX(sel.X), sel.Y - gap - ring.Height);

        // 4. 左侧
        if (sel.X - gap - ring.Width >= 0)
            return new Avalonia.Point(sel.X - gap - ring.Width, ClampY(sel.Y));

        // 5. 内嵌右下角
        var fx = Math.Clamp(sel.Right - ring.Width - gap, sel.X + gap, Math.Max(0, overlay.Width - ring.Width));
        var fy = Math.Clamp(sel.Bottom - ring.Height - gap, sel.Y + gap, Math.Max(0, overlay.Height - ring.Height));
        return new Avalonia.Point(fx, fy);
    }
}
