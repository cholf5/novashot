using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 椭圆描边标注。语义与 <see cref="RectangleAnnotation"/> 对齐：拖拽两对角，椭圆内切该矩形。
/// </summary>
public sealed class EllipseAnnotation : AnnotationBase
{
    private SKRect _rect;

    public SKRect Rect
    {
        get => _rect;
        set => _rect = value.Standardized;
    }

    public EllipseAnnotation(SKRect rect, AnnotationStyle style) : base(style)
    {
        _rect = rect.Standardized;
    }

    public override SKRect BoundingBox
    {
        get
        {
            var pad = Style.StrokeWidth * 0.5f;
            var r = _rect;
            r.Inflate(pad, pad);
            return r;
        }
    }

    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Style.Stroke,
            StrokeWidth = Style.StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawOval(_rect, paint);

        if (Style.Fill is { } fillColor)
        {
            using var fill = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawOval(_rect, fill);
        }
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        // 参数方程：((x-cx)/rx)^2 + ((y-cy)/ry)^2
        // < 1 = 内部；= 1 = 边线；> 1 = 外部
        var cx = _rect.MidX;
        var cy = _rect.MidY;
        var rx = _rect.Width * 0.5f;
        var ry = _rect.Height * 0.5f;
        if (rx <= 0 || ry <= 0) return false;

        var dx = point.X - cx;
        var dy = point.Y - cy;
        var f = (dx / rx) * (dx / rx) + (dy / ry) * (dy / ry);

        if (Style.Fill.HasValue && f <= 1f) return true;

        // 描边命中：半个 stroke + slop 的容差换算成相对 rx/ry 的容差
        var half = Style.StrokeWidth * 0.5f + slop;
        var rMin = Math.Max(1e-3f, Math.Min(rx, ry));
        var tol = half / rMin;
        var lo = (1f - tol) * (1f - tol);
        var hi = (1f + tol) * (1f + tol);
        return f >= lo && f <= hi;
    }

    public override IReadOnlyList<AnnotationHandle> GetHandles() => BoxHandles.GetHandles(_rect);

    public override bool HitTestBody(SKPoint point, float slop)
    {
        // Select 工具下，闭合椭圆整个 body 都算命中。
        var cx = _rect.MidX;
        var cy = _rect.MidY;
        var rx = _rect.Width * 0.5f + slop;
        var ry = _rect.Height * 0.5f + slop;
        if (rx <= 0 || ry <= 0) return false;
        var dx = point.X - cx;
        var dy = point.Y - cy;
        return (dx / rx) * (dx / rx) + (dy / ry) * (dy / ry) <= 1f;
    }

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        _rect = BoxHandles.ApplyHandle(_rect, kind, newImagePos).Standardized;
    }

    public override void MoveBy(SKPoint delta)
    {
        _rect.Offset(delta.X, delta.Y);
    }
}
