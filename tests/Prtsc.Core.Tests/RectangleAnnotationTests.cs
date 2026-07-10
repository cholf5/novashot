using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class RectangleAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f) =>
        new(new SKColor(255, 0, 0), w);

    [Fact]
    public void Constructor_normalizes_negative_rect()
    {
        var a = new RectangleAnnotation(new SKRect(50, 60, 10, 20), Style());
        Assert.Equal(10, a.Rect.Left);
        Assert.Equal(20, a.Rect.Top);
        Assert.Equal(50, a.Rect.Right);
        Assert.Equal(60, a.Rect.Bottom);
    }

    [Fact]
    public void BoundingBox_inflates_by_half_stroke()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 40, 40), Style(4));
        Assert.Equal(new SKRect(8, 8, 42, 42), a.BoundingBox);
    }

    [Fact]
    public void HitTest_hits_stroke_within_slop()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 40, 40), Style(2));
        // 边线上：命中
        Assert.True(a.HitTest(new SKPoint(10, 25), slop: 0f));
        // 稍微外一点，slop 覆盖：命中
        Assert.True(a.HitTest(new SKPoint(8, 25), slop: 2f));
        // 太远：不命中
        Assert.False(a.HitTest(new SKPoint(4, 25), slop: 2f));
    }

    [Fact]
    public void HitTest_ignores_interior_when_no_fill()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 40, 40), Style(2));
        Assert.False(a.HitTest(new SKPoint(25, 25), slop: 0f));
    }

    [Fact]
    public void HitTest_hits_interior_when_filled()
    {
        var style = new AnnotationStyle(
            Stroke: new SKColor(255, 0, 0),
            StrokeWidth: 2f,
            Fill: new SKColor(0, 0, 0, 128));
        var a = new RectangleAnnotation(new SKRect(10, 10, 40, 40), style);
        Assert.True(a.HitTest(new SKPoint(25, 25), slop: 0f));
    }
}
