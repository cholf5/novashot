using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class LineAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f) =>
        new(new SKColor(255, 0, 0), w);

    [Fact]
    public void Endpoints_are_preserved_without_normalization()
    {
        // 与 Rectangle/Ellipse 不同：端点顺序有意义（箭头方向依赖）
        var a = new LineAnnotation(new SKPoint(50, 60), new SKPoint(10, 20), Style());
        Assert.Equal(new SKPoint(50, 60), a.Start);
        Assert.Equal(new SKPoint(10, 20), a.End);
    }

    [Fact]
    public void BoundingBox_covers_both_endpoints_with_pad()
    {
        var a = new LineAnnotation(new SKPoint(10, 20), new SKPoint(40, 50), Style(4));
        // pad = half stroke + 1 = 3
        var b = a.BoundingBox;
        Assert.Equal(7, b.Left);
        Assert.Equal(17, b.Top);
        Assert.Equal(43, b.Right);
        Assert.Equal(53, b.Bottom);
    }

    [Fact]
    public void HitTest_hits_midpoint_of_segment()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        Assert.True(a.HitTest(new SKPoint(30, 10), slop: 0f));
    }

    [Fact]
    public void HitTest_uses_slop_perpendicular()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        // 垂直距离 2px：half stroke (1) + slop (1.5) 覆盖
        Assert.True(a.HitTest(new SKPoint(30, 12), slop: 1.5f));
        // 垂直距离 5px：超过阈值
        Assert.False(a.HitTest(new SKPoint(30, 15), slop: 1.5f));
    }

    [Fact]
    public void HitTest_returns_false_beyond_endpoints()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 10), Style(2));
        Assert.False(a.HitTest(new SKPoint(5, 10), slop: 1f));
        Assert.False(a.HitTest(new SKPoint(55, 10), slop: 1f));
    }

    [Fact]
    public void HitTest_handles_zero_length_line()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(10, 10), Style(2));
        Assert.True(a.HitTest(new SKPoint(10, 10), slop: 0f));
        Assert.False(a.HitTest(new SKPoint(30, 30), slop: 0f));
    }
}
