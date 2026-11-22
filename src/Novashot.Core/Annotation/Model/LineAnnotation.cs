using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

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

    /// <summary>点到线段的最短距离。M5a 起委托 <see cref="PolylineGeometry"/>（与 Pencil/Highlighter 共享）。</summary>
    protected static float DistancePointToSegment(SKPoint p, SKPoint a, SKPoint b)
        => PolylineGeometry.DistanceToSegment(p, a, b);

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
