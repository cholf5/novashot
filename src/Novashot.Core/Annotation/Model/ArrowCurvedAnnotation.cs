using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 曲箭头：默认情况下控制点自动取 <see cref="Start"/>-<see cref="End"/> 中点向垂直方向偏移
/// <see cref="SagRatio"/> × 线段长度，形成一段二次贝塞尔弧。
///
/// M4d 起支持"三点手动控制"：拖动 <see cref="HandleKind.Control"/> 手柄会把控制点固化为绝对坐标
/// （<see cref="HasCustomControlPoint"/> 变 true）。此后 <see cref="MoveHandle"/> Start/End 不改控制点
/// （视觉上曲线随端点走但控制点保持屏上位置），整体 <see cref="MoveBy"/> 会同步平移。
/// 调用 <see cref="ResetControlPoint"/> 回到默认推导。
/// </summary>
public sealed class ArrowCurvedAnnotation : LineAnnotation
{
    /// <summary>控制点垂直偏移量占线段长度比例。0.18 视觉上类似 Flameshot Marker 的默认弧度。</summary>
    public const float SagRatio = 0.18f;

    private SKPoint? _customControl;

    public ArrowCurvedAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        : base(start, end, style) { }

    /// <summary>true = 控制点已被用户手动设置，覆盖推导值。</summary>
    public bool HasCustomControlPoint => _customControl.HasValue;

    /// <summary>
    /// 二次贝塞尔控制点。默认由 <see cref="Start"/>/<see cref="End"/> 推导；
    /// 一旦 setter 被调用（或 MoveHandle Control）则固化为绝对坐标，直到 <see cref="ResetControlPoint"/>。
    /// </summary>
    public SKPoint ControlPoint
    {
        get => _customControl ?? ComputeDerivedControlPoint();
        set => _customControl = value;
    }

    /// <summary>清除自定义控制点，回落到 Start/End 推导公式。</summary>
    public void ResetControlPoint() => _customControl = null;

    private SKPoint ComputeDerivedControlPoint()
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

    public override IReadOnlyList<AnnotationHandle> GetHandles() => new[]
    {
        new AnnotationHandle(HandleKind.Start,   Start),
        new AnnotationHandle(HandleKind.End,     End),
        new AnnotationHandle(HandleKind.Control, ControlPoint),
    };

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        if (kind == HandleKind.Control)
        {
            _customControl = newImagePos;
            return;
        }
        base.MoveHandle(kind, newImagePos);
    }

    public override void MoveBy(SKPoint delta)
    {
        base.MoveBy(delta);
        if (_customControl is { } cp)
            _customControl = new SKPoint(cp.X + delta.X, cp.Y + delta.Y);
    }
}
