using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class FreehandAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f) => new(new SKColor(255, 0, 0), w);

    private static readonly SKPoint[] Zigzag =
    {
        new(10, 10), new(40, 10), new(40, 40),
    };

    [Fact]
    public void Points_are_preserved_in_order()
    {
        var a = new PencilAnnotation(Zigzag, Style());
        Assert.Equal(Zigzag, a.Points.ToArray());
    }

    [Fact]
    public void BoundingBox_covers_points_with_stroke_pad()
    {
        // pad = half stroke (1) + 1 = 2
        var a = new PencilAnnotation(Zigzag, Style(2));
        var b = a.BoundingBox;
        Assert.Equal(8, b.Left);
        Assert.Equal(8, b.Top);
        Assert.Equal(42, b.Right);
        Assert.Equal(42, b.Bottom);
    }

    [Fact]
    public void Highlighter_BoundingBox_adds_width_bonus()
    {
        // 有效宽 = 2 + 14 = 16 → pad = 8 + 1 = 9
        var a = new HighlighterAnnotation(Zigzag, Style(2));
        var b = a.BoundingBox;
        Assert.Equal(1, b.Left);
        Assert.Equal(1, b.Top);
        Assert.Equal(49, b.Right);
        Assert.Equal(49, b.Bottom);
    }

    [Fact]
    public void HitTest_on_path_succeeds_and_far_away_fails()
    {
        var a = new PencilAnnotation(Zigzag, Style(2));
        Assert.True(a.HitTest(new SKPoint(25, 10), slop: 0f));
        Assert.False(a.HitTest(new SKPoint(25, 30), slop: 0f));
    }

    [Fact]
    public void Highlighter_HitTest_uses_effective_width()
    {
        // 垂直距离 6px：pencil（half=1）不中；highlighter（half=8）命中
        var pencil = new PencilAnnotation(Zigzag, Style(2));
        var high = new HighlighterAnnotation(Zigzag, Style(2));
        var probe = new SKPoint(25, 16);
        Assert.False(pencil.HitTest(probe, slop: 0f));
        Assert.True(high.HitTest(probe, slop: 0f));
    }

    [Fact]
    public void GetHandles_is_empty_and_MoveHandle_is_noop()
    {
        var a = new PencilAnnotation(Zigzag, Style());
        Assert.Empty(a.GetHandles());
        a.MoveHandle(Prtsc.Core.Annotation.Editing.HandleKind.Start, new SKPoint(0, 0));
        Assert.Equal(Zigzag, a.Points.ToArray());
    }

    [Fact]
    public void MoveBy_translates_all_points()
    {
        var a = new PencilAnnotation(Zigzag, Style());
        a.MoveBy(new SKPoint(5, -3));
        Assert.Equal(new SKPoint(15, 7), a.Points[0]);
        Assert.Equal(new SKPoint(45, 7), a.Points[1]);
        Assert.Equal(new SKPoint(45, 37), a.Points[2]);
    }

    [Fact]
    public void AppendPoint_extends_points()
    {
        var a = new PencilAnnotation(new[] { new SKPoint(0, 0) }, Style());
        a.AppendPoint(new SKPoint(10, 0));
        Assert.Equal(2, a.Points.Count);
        Assert.Equal(new SKPoint(10, 0), a.Points[1]);
    }
}
