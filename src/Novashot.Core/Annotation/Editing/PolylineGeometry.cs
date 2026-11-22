using SkiaSharp;

namespace Novashot.Core.Annotation.Editing;

/// <summary>
/// 折线几何纯函数：M5a 起供 Line / Pencil / Highlighter 等线状标注共享。
/// 与 <see cref="LineAnnotation"/> 原有的 protected 实现等价；Line 已改为委托此处。
/// </summary>
public static class PolylineGeometry
{
    /// <summary>点到线段 (a→b) 的最短距离（垂足 clamp 到 [0,1]；零长线段退化为点到点）。</summary>
    public static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;
        if (lenSq < 1e-6f)
        {
            var dx0 = p.X - a.X;
            var dy0 = p.Y - a.Y;
            return MathF.Sqrt(dx0 * dx0 + dy0 * dy0);
        }
        var t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        var qx = a.X + t * abx;
        var qy = a.Y + t * aby;
        var dx = p.X - qx;
        var dy = p.Y - qy;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>点到折线（按顺序连接 points）的最短距离。少于 2 点按单点处理。</summary>
    public static float DistanceToPolyline(SKPoint p, IReadOnlyList<SKPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return float.PositiveInfinity;
        if (points.Count == 1)
        {
            var dx = p.X - points[0].X;
            var dy = p.Y - points[0].Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        var min = float.PositiveInfinity;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var d = DistanceToSegment(p, points[i], points[i + 1]);
            if (d < min) min = d;
        }
        return min;
    }

    /// <summary>折线的累计长度（各段欧氏距离之和）。</summary>
    public static float PathLength(IReadOnlyList<SKPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var len = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var dx = points[i + 1].X - points[i].X;
            var dy = points[i + 1].Y - points[i].Y;
            len += MathF.Sqrt(dx * dx + dy * dy);
        }
        return len;
    }
}
