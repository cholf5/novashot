using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class RectangleToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Forward_drag_yields_standardized_rect()
    {
        var t = new RectangleTool();
        t.OnPointerDown(new SKPoint(10, 20), Style());
        t.OnPointerMove(new SKPoint(50, 60));
        var a = t.OnPointerUp(new SKPoint(50, 60));

        var rect = Assert.IsType<RectangleAnnotation>(a);
        Assert.Equal(new SKRect(10, 20, 50, 60), rect.Rect);
        Assert.Null(t.Preview);
    }

    [Fact]
    public void Reverse_drag_yields_standardized_rect()
    {
        var t = new RectangleTool();
        t.OnPointerDown(new SKPoint(80, 90), Style());
        t.OnPointerMove(new SKPoint(20, 30));
        var a = t.OnPointerUp(new SKPoint(20, 30));

        var rect = Assert.IsType<RectangleAnnotation>(a);
        Assert.Equal(new SKRect(20, 30, 80, 90), rect.Rect);
    }

    [Fact]
    public void Sub_min_area_drag_returns_null()
    {
        var t = new RectangleTool();
        t.OnPointerDown(new SKPoint(10, 10), Style());
        // 宽 = 1 px (< MinAreaPx = 2)
        var a = t.OnPointerUp(new SKPoint(11, 20));
        Assert.Null(a);
    }

    [Fact]
    public void Preview_updates_on_move()
    {
        var t = new RectangleTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        Assert.NotNull(t.Preview);

        t.OnPointerMove(new SKPoint(30, 40));
        var preview = Assert.IsType<RectangleAnnotation>(t.Preview);
        Assert.Equal(new SKRect(0, 0, 30, 40), preview.Rect);
    }

    [Fact]
    public void Cancel_clears_preview_and_prevents_commit()
    {
        var t = new RectangleTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        t.OnPointerMove(new SKPoint(50, 50));
        t.Cancel();

        Assert.Null(t.Preview);
        // Cancel 后再 up 也不应产出
        Assert.Null(t.OnPointerUp(new SKPoint(50, 50)));
    }

    [Fact]
    public void PointerUp_without_Down_returns_null()
    {
        var t = new RectangleTool();
        Assert.Null(t.OnPointerUp(new SKPoint(10, 10)));
    }
}
