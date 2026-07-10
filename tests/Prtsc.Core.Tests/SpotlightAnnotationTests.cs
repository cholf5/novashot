using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class SpotlightAnnotationTests
{
    private static AnnotationStyle Style(float w = 0f) => new(new SKColor(255, 255, 255), w);

    [Fact]
    public void Constructor_normalizes_negative_region()
    {
        var a = new SpotlightAnnotation(new SKRect(200, 300, 50, 100), 1920, 1080, Style());
        Assert.Equal(50, a.Region.Left);
        Assert.Equal(100, a.Region.Top);
        Assert.Equal(200, a.Region.Right);
        Assert.Equal(300, a.Region.Bottom);
    }

    [Fact]
    public void BoundingBox_covers_full_image()
    {
        var a = new SpotlightAnnotation(new SKRect(10, 10, 50, 50), 1920, 1080, Style());
        Assert.Equal(new SKRect(0, 0, 1920, 1080), a.BoundingBox);
    }

    [Fact]
    public void HitTest_hits_dark_area_outside_region()
    {
        var a = new SpotlightAnnotation(new SKRect(100, 100, 200, 200), 1920, 1080, Style());
        Assert.True(a.HitTest(new SKPoint(500, 500), slop: 0f));
    }

    [Fact]
    public void HitTest_misses_inside_bright_region()
    {
        var a = new SpotlightAnnotation(new SKRect(100, 100, 200, 200), 1920, 1080, Style());
        Assert.False(a.HitTest(new SKPoint(150, 150), slop: 0f));
    }

    [Fact]
    public void HitTest_misses_outside_image_bounds()
    {
        var a = new SpotlightAnnotation(new SKRect(100, 100, 200, 200), 1920, 1080, Style());
        Assert.False(a.HitTest(new SKPoint(-1, -1), slop: 0f));
        Assert.False(a.HitTest(new SKPoint(2000, 500), slop: 0f));
    }

    [Fact]
    public void Default_overlay_color_is_semi_transparent_black()
    {
        Assert.Equal(0, SpotlightAnnotation.DefaultOverlayColor.Red);
        Assert.Equal(0, SpotlightAnnotation.DefaultOverlayColor.Green);
        Assert.Equal(0, SpotlightAnnotation.DefaultOverlayColor.Blue);
        Assert.Equal(0x99, SpotlightAnnotation.DefaultOverlayColor.Alpha);
    }
}
