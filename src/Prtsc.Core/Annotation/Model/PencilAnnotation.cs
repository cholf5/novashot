using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 铅笔手绘：普通不透明实线描边，Round cap/join。
/// </summary>
public sealed class PencilAnnotation : FreehandAnnotationBase
{
    public PencilAnnotation(IEnumerable<SKPoint> points, AnnotationStyle style) : base(points, style)
    {
    }

    public override void Render(SKCanvas canvas)
    {
        if (Points.Count == 0) return;
        using var path = BuildPath();
        using var paint = new SKPaint
        {
            Color = Style.Stroke,
            StrokeWidth = Style.StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, paint);
    }
}
