using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class TextAnnotationTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Constructor_defaults_font_family_when_null()
    {
        var a = new TextAnnotation(new SKPoint(10, 30), "hi", 24f, Style(), fontFamily: null);
        Assert.Equal(TextAnnotation.DefaultFontFamily, a.FontFamily);
    }

    [Fact]
    public void Constructor_normalizes_null_text_to_empty()
    {
        var a = new TextAnnotation(new SKPoint(0, 0), null!, 24f, Style());
        Assert.Equal(string.Empty, a.Text);
    }

    [Fact]
    public void BoundingBox_offsets_to_position()
    {
        var a = new TextAnnotation(new SKPoint(100, 200), "Hello", 24f, Style());
        var b = a.BoundingBox;
        // 文字度量随字体不同，但 BoundingBox 应在 Position 附近（水平方向近似锚点 X）
        Assert.InRange(b.Left, 90, 110);
        // 基线在 Y，字符主体在 baseline 上方（Top 应低于 Y）
        Assert.True(b.Top < 200);
        Assert.True(b.Bottom >= 200 - 5); // 允许下伸部
    }

    [Fact]
    public void BoundingBox_updates_when_text_changes()
    {
        var a = new TextAnnotation(new SKPoint(0, 30), "a", 24f, Style());
        var w1 = a.BoundingBox.Width;
        a.Text = "aaaaaaaaaa";
        var w2 = a.BoundingBox.Width;
        Assert.True(w2 > w1);
    }

    [Fact]
    public void HitTest_hits_within_bounding_box()
    {
        var a = new TextAnnotation(new SKPoint(100, 200), "Hello", 24f, Style());
        var b = a.BoundingBox;
        var center = new SKPoint((b.Left + b.Right) * 0.5f, (b.Top + b.Bottom) * 0.5f);
        Assert.True(a.HitTest(center, slop: 0f));
    }

    [Fact]
    public void HitTest_misses_far_from_bounding_box()
    {
        var a = new TextAnnotation(new SKPoint(100, 200), "Hello", 24f, Style());
        Assert.False(a.HitTest(new SKPoint(5000, 5000), slop: 0f));
    }
}
