using Prtsc.Core.Annotation.Editing;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 直线段标注。<see cref="Start"/>、<see cref="End"/> 为端点；不做规范化（端点顺序有意义，
/// 例如未来 Arrow 会用 <see cref="End"/> 作为箭头方向）。
/// </summary>
public class LineAnnotation : AnnotationBase
{
    public SKPoint Start { get; set; }
    public SKPoint End { get; set; }

    public LineAnnotation(SKPoint start, SKPoint end, AnnotationStyle style) : base(style)
    {
        Start = start;
        End = end;
    }

    public override SKRect BoundingBox
    {
        get
        {
            var pad = Style.StrokeWidth * 0.5f + 1f;
            var l = Math.Min(Start.X, End.X) - pad;
            var t = Math.Min(Start.Y, End.Y) - pad;
            var r = Math.Max(Start.X, End.X) + pad;
            var b = Math.Max(Start.Y, End.Y) + pad;
            return new SKRect(l, t, r, b);
        }
    }

    public override void Render(SKCanvas canvas)
    {
        using var paint = MakeStrokePaint();
        canvas.DrawLine(Start, End, paint);
    }

    protected SKPaint MakeStrokePaint() => new()
    {
        Color = Style.Stroke,
        StrokeWidth = Style.StrokeWidth,
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
    };

    public override bool HitTest(SKPoint point, float slop)
    {
        var d = DistancePointToSegment(point, Start, End);
        return d <= Style.StrokeWidth * 0.5f + slop;
    }

    /// <summary>点到线段的最短距离。M4b/M4c 的 Line/Arrow HitTest 都会用它。</summary>
    protected static float DistancePointToSegment(SKPoint p, SKPoint a, SKPoint b)
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

    public override IReadOnlyList<AnnotationHandle> GetHandles() => new[]
    {
        new AnnotationHandle(HandleKind.Start, Start),
        new AnnotationHandle(HandleKind.End,   End),
    };

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        switch (kind)
        {
            case HandleKind.Start: Start = newImagePos; break;
            case HandleKind.End:   End   = newImagePos; break;
        }
    }

    public override void MoveBy(SKPoint delta)
    {
        Start = new SKPoint(Start.X + delta.X, Start.Y + delta.Y);
        End   = new SKPoint(End.X + delta.X,   End.Y + delta.Y);
    }
}
