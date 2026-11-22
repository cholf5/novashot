using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class ArrowAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f) =>
        new(new SKColor(255, 0, 0), w);

    [Fact]
    public void Straight_inherits_line_hit_test()
    {
        var a = new ArrowStraightAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        Assert.True(a.HitTest(new SKPoint(30, 10), slop: 0f));
    }

    [Fact]
    public void Straight_bounding_box_pads_for_arrowhead()
    {
        var line = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        var arrow = new ArrowStraightAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        // 箭头 BoundingBox 至少不小于纯线段的 BoundingBox
        var lb = line.BoundingBox;
        var ab = arrow.BoundingBox;
        Assert.True(ab.Left <= lb.Left);
        Assert.True(ab.Top <= lb.Top);
        Assert.True(ab.Right >= lb.Right);
        Assert.True(ab.Bottom >= lb.Bottom);
        // 且比纯线段更宽（预留头部 halfWidth = 3*strokeWidth = 6 的裕度）
        Assert.True(ab.Width > lb.Width);
    }

    [Fact]
    public void Curved_control_point_is_perpendicular_offset_from_midpoint()
    {
        // 水平线 (0,0) → (100,0)：中点 (50,0)，法向 (逆时针 90°) 是 (0, 1)，
        // 偏移 100 * 0.18 = 18 → 控制点 (50, 18)
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style(2));
        var ctrl = a.ControlPoint;
        Assert.Equal(50f, ctrl.X, 3);
        Assert.Equal(18f, ctrl.Y, 3);
    }

    [Fact]
    public void Curved_control_point_falls_back_to_midpoint_for_zero_length()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(10, 10), new SKPoint(10, 10), Style(2));
        Assert.Equal(new SKPoint(10, 10), a.ControlPoint);
    }

    [Fact]
    public void Curved_hit_test_hits_bezier_apex()
    {
        // 水平 0-100，控制点 (50, 18)，t=0.5 时 y = 0.25*0 + 0.5*18 + 0.25*0 = 9
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style(2));
        Assert.True(a.HitTest(new SKPoint(50, 9), slop: 1f));
        // 远离曲线：不命中
        Assert.False(a.HitTest(new SKPoint(50, 30), slop: 1f));
    }

    [Fact]
    public void Curved_bounding_box_contains_control_point()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style(2));
        var ctrl = a.ControlPoint;
        var b = a.BoundingBox;
        Assert.True(b.Left <= ctrl.X && ctrl.X <= b.Right);
        Assert.True(b.Top <= ctrl.Y && ctrl.Y <= b.Bottom);
    }
}
