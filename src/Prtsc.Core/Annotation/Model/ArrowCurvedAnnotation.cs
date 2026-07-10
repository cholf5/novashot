using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 曲箭头：单次拖拽版本，控制点自动取 <see cref="Start"/>-<see cref="End"/> 中点向垂直方向偏移
/// <see cref="SagRatio"/> × 线段长度，形成一段二次贝塞尔弧。M4c/M4d 会补三点手动控制点交互。
/// </summary>
public sealed class ArrowCurvedAnnotation : LineAnnotation
{
    /// <summary>控制点垂直偏移量占线段长度比例。0.18 视觉上类似 Flameshot Marker 的默认弧度。</summary>
    public const float SagRatio = 0.18f;

    public ArrowCurvedAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        : base(start, end, style) { }

    /// <summary>由 <see cref="Start"/> / <see cref="End"/> 推算的二次贝塞尔控制点。</summary>
    public SKPoint ControlPoint
    {
        get
        {
            var mid = new SKPoint((Start.X + End.X) * 0.5f, (Start.Y + End.Y) * 0.5f);
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return mid;
            // 逆时针 90°：法向 (-dy, dx) / len
            var nx = -dy / len;
            var ny = dx / len;
            var off = len * SagRatio;
            return new SKPoint(mid.X + nx * off, mid.Y + ny * off);
        }
    }

    public override SKRect BoundingBox
    {
        get
        {
            var b = base.BoundingBox;
            var ctrl = ControlPoint;
            var extra = Style.StrokeWidth * ArrowStraightAnnotation.HeadHalfWidthMul + 2f;
            var l = Math.Min(b.Left, ctrl.X - extra);
            var t = Math.Min(b.Top, ctrl.Y - extra);
            var r = Math.Max(b.Right, ctrl.X + extra);
            var bot = Math.Max(b.Bottom, ctrl.Y + extra);
            return new SKRect(l, t, r, bot);
        }
    }

    public override void Render(SKCanvas canvas)
    {
        using var paint = MakeStrokePaint();
        var ctrl = ControlPoint;

        // 曲线：稍缩短末端，与箭头三角形对接
        var dx = End.X - Start.X;
        var dy = End.Y - Start.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f)
        {
            canvas.DrawLine(Start, End, paint);
            return;
        }

        var headLen = Style.StrokeWidth * ArrowStraightAnnotation.HeadLengthMul;
        var headHalf = Style.StrokeWidth * ArrowStraightAnnotation.HeadHalfWidthMul;

        // 端点切线（二次贝塞尔在 t=1 的切向） = End - Ctrl
        var tx = End.X - ctrl.X;
        var ty = End.Y - ctrl.Y;
        var tlen = MathF.Sqrt(tx * tx + ty * ty);
        if (tlen < 1e-3f) { tx = dx; ty = dy; tlen = len; }
        var ux = tx / tlen;
        var uy = ty / tlen;

        // 曲线画到"近末端"点，避免和箭头三角重叠
        var lineEnd = new SKPoint(End.X - ux * headLen * 0.5f, End.Y - uy * headLen * 0.5f);
        using (var path = new SKPath())
        {
            path.MoveTo(Start);
            path.QuadTo(ctrl, lineEnd);
            canvas.DrawPath(path, paint);
        }

        // 箭头三角
        var baseCenter = new SKPoint(End.X - ux * headLen, End.Y - uy * headLen);
        var nx = -uy;
        var ny = ux;
        var p1 = new SKPoint(baseCenter.X + nx * headHalf, baseCenter.Y + ny * headHalf);
        var p2 = new SKPoint(baseCenter.X - nx * headHalf, baseCenter.Y - ny * headHalf);
        using var fill = new SKPaint { Color = Style.Stroke, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var tri = new SKPath();
        tri.MoveTo(End);
        tri.LineTo(p1);
        tri.LineTo(p2);
        tri.Close();
        canvas.DrawPath(tri, fill);
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        // 二次贝塞尔离散化为 16 段折线，最近段距离 ≤ stroke/2 + slop 即命中
        const int Steps = 16;
        var ctrl = ControlPoint;
        var prev = Start;
        var thr = Style.StrokeWidth * 0.5f + slop;
        for (int i = 1; i <= Steps; i++)
        {
            var t = i / (float)Steps;
            var omt = 1f - t;
            var x = omt * omt * Start.X + 2f * omt * t * ctrl.X + t * t * End.X;
            var y = omt * omt * Start.Y + 2f * omt * t * ctrl.Y + t * t * End.Y;
            var cur = new SKPoint(x, y);
            if (DistancePointToSegment(point, prev, cur) <= thr) return true;
            prev = cur;
        }
        return false;
    }
}
