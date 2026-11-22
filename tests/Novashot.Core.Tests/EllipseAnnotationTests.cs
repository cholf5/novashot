using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class EllipseAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f, SKColor? fill = null) =>
        new(new SKColor(255, 0, 0), w, fill);

    [Fact]
    public void Constructor_normalizes_negative_rect()
    {
        var a = new EllipseAnnotation(new SKRect(50, 60, 10, 20), Style());
        Assert.Equal(10, a.Rect.Left);
        Assert.Equal(20, a.Rect.Top);
        Assert.Equal(50, a.Rect.Right);
        Assert.Equal(60, a.Rect.Bottom);
    }

    [Fact]
    public void BoundingBox_inflates_by_half_stroke()
    {
        var a = new EllipseAnnotation(new SKRect(10, 10, 40, 40), Style(4));
        Assert.Equal(new SKRect(8, 8, 42, 42), a.BoundingBox);
    }

    [Fact]
    public void HitTest_hits_edge_within_slop()
    {
        // 中心 (25,25) rx=ry=15；正右侧边线 (40,25)
        var a = new EllipseAnnotation(new SKRect(10, 10, 40, 40), Style(2));
        Assert.True(a.HitTest(new SKPoint(40, 25), slop: 0f));
        Assert.True(a.HitTest(new SKPoint(42, 25), slop: 2f));
        Assert.False(a.HitTest(new SKPoint(50, 25), slop: 2f));
    }

    [Fact]
    public void HitTest_ignores_interior_when_no_fill()
    {
        var a = new EllipseAnnotation(new SKRect(10, 10, 40, 40), Style(2));
        Assert.False(a.HitTest(new SKPoint(25, 25), slop: 0f));
    }

    [Fact]
    public void HitTest_hits_interior_when_filled()
    {
        var a = new EllipseAnnotation(new SKRect(10, 10, 40, 40), Style(2, new SKColor(0, 0, 0, 128)));
        Assert.True(a.HitTest(new SKPoint(25, 25), slop: 0f));
    }

    [Fact]
    public void HitTest_returns_false_for_degenerate_ellipse()
    {
        var a = new EllipseAnnotation(new SKRect(10, 10, 10, 10), Style(2));
        Assert.False(a.HitTest(new SKPoint(10, 10), slop: 0f));
    }
}
