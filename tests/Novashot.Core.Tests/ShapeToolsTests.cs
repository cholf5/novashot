using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class EllipseToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Drag_produces_standardized_ellipse()
    {
        var t = new EllipseTool();
        t.OnPointerDown(new SKPoint(50, 60), Style());
        t.OnPointerMove(new SKPoint(10, 20));
        var a = (EllipseAnnotation)t.OnPointerUp(new SKPoint(10, 20))!;
        Assert.Equal(10, a.Rect.Left);
        Assert.Equal(20, a.Rect.Top);
    }

    [Fact]
    public void Below_min_area_returns_null()
    {
        var t = new EllipseTool();
        t.OnPointerDown(new SKPoint(10, 10), Style());
        Assert.Null(t.OnPointerUp(new SKPoint(11, 11)));
    }

    [Fact]
    public void Preview_updates_during_drag()
    {
        var t = new EllipseTool();
        t.OnPointerDown(new SKPoint(10, 10), Style());
        Assert.NotNull(t.Preview);
        t.OnPointerMove(new SKPoint(30, 40));
        var p = (EllipseAnnotation)t.Preview!;
        Assert.Equal(new SKRect(10, 10, 30, 40), p.Rect);
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var t = new EllipseTool();
        t.OnPointerDown(new SKPoint(10, 10), Style());
        t.Cancel();
        Assert.Null(t.Preview);
    }
}

public class TwoPointToolsTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void LineTool_creates_line_with_original_endpoints()
    {
        var t = new LineTool();
        t.OnPointerDown(new SKPoint(50, 60), Style());
        t.OnPointerMove(new SKPoint(10, 20));
        var a = (LineAnnotation)t.OnPointerUp(new SKPoint(10, 20))!;
        Assert.Equal(new SKPoint(50, 60), a.Start);
        Assert.Equal(new SKPoint(10, 20), a.End);
    }

    [Fact]
    public void ArrowStraightTool_creates_arrow()
    {
        var t = new ArrowStraightTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        var a = t.OnPointerUp(new SKPoint(100, 0));
        Assert.IsType<ArrowStraightAnnotation>(a);
    }

    [Fact]
    public void ArrowCurvedTool_creates_curved_arrow()
    {
        var t = new ArrowCurvedTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        var a = t.OnPointerUp(new SKPoint(100, 0));
        Assert.IsType<ArrowCurvedAnnotation>(a);
    }

    [Fact]
    public void Below_min_length_returns_null()
    {
        var t = new LineTool();
        t.OnPointerDown(new SKPoint(10, 10), Style());
        Assert.Null(t.OnPointerUp(new SKPoint(11, 11))); // sqrt(2) < 2
    }

    [Fact]
    public void Preview_reflects_current_endpoint()
    {
        var t = new LineTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        t.OnPointerMove(new SKPoint(30, 40));
        var p = (LineAnnotation)t.Preview!;
        Assert.Equal(new SKPoint(30, 40), p.End);
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var t = new LineTool();
        t.OnPointerDown(new SKPoint(0, 0), Style());
        t.Cancel();
        Assert.Null(t.Preview);
    }
}

public class TextToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Click_creates_placeholder_text_annotation()
    {
        var t = new TextTool();
        t.OnPointerDown(new SKPoint(100, 100), Style());
        var a = (TextAnnotation)t.OnPointerUp(new SKPoint(100, 100))!;
        Assert.Equal(TextTool.PlaceholderText, a.Text);
        Assert.Equal(TextTool.DefaultFontSize, a.FontSize);
    }

    [Fact]
    public void Preview_is_present_after_pointer_down()
    {
        var t = new TextTool();
        t.OnPointerDown(new SKPoint(50, 50), Style());
        Assert.NotNull(t.Preview);
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var t = new TextTool();
        t.OnPointerDown(new SKPoint(50, 50), Style());
        t.Cancel();
        Assert.Null(t.Preview);
    }
}

public class SpotlightToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 255, 255), 0f);

    [Fact]
    public void Drag_produces_standardized_spotlight()
    {
        var t = new SpotlightTool(1920, 1080);
        t.OnPointerDown(new SKPoint(200, 300), Style());
        t.OnPointerMove(new SKPoint(50, 100));
        var a = (SpotlightAnnotation)t.OnPointerUp(new SKPoint(50, 100))!;
        Assert.Equal(50, a.Region.Left);
        Assert.Equal(100, a.Region.Top);
        Assert.Equal(200, a.Region.Right);
        Assert.Equal(300, a.Region.Bottom);
    }

    [Fact]
    public void Spotlight_carries_image_dimensions()
    {
        var t = new SpotlightTool(1920, 1080);
        t.OnPointerDown(new SKPoint(10, 10), Style());
        var a = (SpotlightAnnotation)t.OnPointerUp(new SKPoint(500, 500))!;
        Assert.Equal(1920, a.ImageWidth);
        Assert.Equal(1080, a.ImageHeight);
    }

    [Fact]
    public void Below_min_area_returns_null()
    {
        var t = new SpotlightTool(1920, 1080);
        t.OnPointerDown(new SKPoint(10, 10), Style());
        Assert.Null(t.OnPointerUp(new SKPoint(12, 12)));
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var t = new SpotlightTool(1920, 1080);
        t.OnPointerDown(new SKPoint(10, 10), Style());
        t.Cancel();
        Assert.Null(t.Preview);
    }
}
