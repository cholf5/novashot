using Prtsc.Core.Annotation.Editing;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 矩形描边标注。构造时 <see cref="SKRect.Standardized"/> 一次，
/// 之后 <see cref="Rect"/> 保证 <c>Left ≤ Right</c>、<c>Top ≤ Bottom</c>。
/// </summary>
public sealed class RectangleAnnotation : AnnotationBase
{
    private SKRect _rect;

    public SKRect Rect
    {
        get => _rect;
        set => _rect = value.Standardized;
    }

    public RectangleAnnotation(SKRect rect, AnnotationStyle style) : base(style)
    {
        _rect = rect.Standardized;
    }

    public override SKRect BoundingBox
    {
        get
        {
            // 描边向外扩半个 stroke，避免 dirty 漏渲。
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
            StrokeJoin = SKStrokeJoin.Miter,
        };
        canvas.DrawRect(_rect, paint);

        if (Style.Fill is { } fillColor)
        {
            using var fill = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawRect(_rect, fill);
        }
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        // 命中：填充范围内 或 命中描边区（描边宽度/2 + slop）
        if (Style.Fill.HasValue && _rect.Contains(point))
            return true;

        var half = Style.StrokeWidth * 0.5f + slop;
        var outer = _rect;
        outer.Inflate(half, half);
        if (!outer.Contains(point)) return false;

        var inner = _rect;
        inner.Inflate(-half, -half);
        // 内环退化（矩形过小）时，外环命中即算命中
        if (inner.Width <= 0 || inner.Height <= 0) return true;
        return !inner.Contains(point);
    }

    public override IReadOnlyList<AnnotationHandle> GetHandles() => BoxHandles.GetHandles(_rect);

    public override bool HitTestBody(SKPoint point, float slop)
    {
        // Select 工具下，闭合矩形整个 body 都算命中。
        var r = _rect;
        r.Inflate(slop, slop);
        return r.Contains(point);
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
