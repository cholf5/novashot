using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class NumberAnnotationTests
{
    private static AnnotationStyle Style(float w = 2f, SKColor? fill = null) =>
        new(new SKColor(255, 0, 0), w, fill);

    [Fact]
    public void Radius_defaults_to_stroke_width_times_multiplier()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 1, Style(2));
        Assert.Equal(2 * NumberAnnotation.DefaultRadiusMul, a.Radius);
    }

    [Fact]
    public void Radius_honors_explicit_override()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 1, Style(2), radius: 15f);
        Assert.Equal(15f, a.Radius);
    }

    [Fact]
    public void BoundingBox_covers_disk_plus_one_px_pad()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 1, Style(2), radius: 10f);
        var b = a.BoundingBox;
        Assert.Equal(39, b.Left);
        Assert.Equal(39, b.Top);
        Assert.Equal(61, b.Right);
        Assert.Equal(61, b.Bottom);
    }

    [Fact]
    public void HitTest_hits_inside_disk()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 1, Style(2), radius: 10f);
        Assert.True(a.HitTest(new SKPoint(50, 50), slop: 0f));
        Assert.True(a.HitTest(new SKPoint(58, 50), slop: 0f)); // 8 < 10
    }

    [Fact]
    public void HitTest_misses_outside_disk_and_uses_slop()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 1, Style(2), radius: 10f);
        Assert.False(a.HitTest(new SKPoint(62, 50), slop: 0f)); // 12 > 10
        Assert.True(a.HitTest(new SKPoint(62, 50), slop: 3f));  // 12 <= 10+3
    }
}

public class NumberCounterTests
{
    [Fact]
    public void Peek_does_not_advance()
    {
        var c = new NumberCounter();
        Assert.Equal(1, c.Peek());
        Assert.Equal(1, c.Peek());
    }

    [Fact]
    public void Next_returns_current_and_increments()
    {
        var c = new NumberCounter();
        Assert.Equal(1, c.Next());
        Assert.Equal(2, c.Next());
        Assert.Equal(3, c.Peek());
    }

    [Fact]
    public void Reset_returns_to_start()
    {
        var c = new NumberCounter();
        c.Next(); c.Next(); c.Next();
        c.Reset();
        Assert.Equal(1, c.Peek());
    }

    [Fact]
    public void Reset_accepts_custom_start()
    {
        var c = new NumberCounter();
        c.Reset(10);
        Assert.Equal(10, c.Next());
    }

    [Fact]
    public void Constructor_rejects_start_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumberCounter(0));
    }

    [Fact]
    public void Reset_rejects_start_below_one()
    {
        var c = new NumberCounter();
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Reset(0));
    }
}
