using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 直箭头：起点在尾，终点在头。箭头三角尺寸随 strokeWidth 缩放。
/// </summary>
public sealed class ArrowStraightAnnotation : LineAnnotation
{
    /// <summary>箭头三角形的长度倍数（相对 strokeWidth）。</summary>
    public const float HeadLengthMul = 5f;
    /// <summary>箭头三角形的半宽倍数（相对 strokeWidth）。</summary>
    public const float HeadHalfWidthMul = 3f;

    public ArrowStraightAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        : base(start, end, style) { }

    public override SKRect BoundingBox
    {
        get
        {
            // 起点端不加箭头；终点端加箭头长度的裕度
            var extra = Style.StrokeWidth * HeadHalfWidthMul + 2f;
            var b = base.BoundingBox;
            b.Inflate(extra, extra);
            return b;
        }
    }

    public override void Render(SKCanvas canvas)
    {
        using var paint = MakeStrokePaint();

        var dx = End.X - Start.X;
        var dy = End.Y - Start.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f)
        {
            canvas.DrawLine(Start, End, paint);
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var headLen = Style.StrokeWidth * HeadLengthMul;
        var headHalf = Style.StrokeWidth * HeadHalfWidthMul;

        // 线段末端稍向后缩，避免与箭头三角形边缘打架
        var lineEnd = new SKPoint(End.X - ux * headLen * 0.5f, End.Y - uy * headLen * 0.5f);
        canvas.DrawLine(Start, lineEnd, paint);

        // 箭头三角形
        var baseCenter = new SKPoint(End.X - ux * headLen, End.Y - uy * headLen);
        var nx = -uy;
        var ny = ux;
        var p1 = new SKPoint(baseCenter.X + nx * headHalf, baseCenter.Y + ny * headHalf);
        var p2 = new SKPoint(baseCenter.X - nx * headHalf, baseCenter.Y - ny * headHalf);

        using var fill = new SKPaint
        {
            Color = Style.Stroke,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var path = new SKPath();
        path.MoveTo(End);
        path.LineTo(p1);
        path.LineTo(p2);
        path.Close();
        canvas.DrawPath(path, fill);
    }
}
