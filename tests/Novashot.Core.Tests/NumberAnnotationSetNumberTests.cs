using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;
using Xunit;

namespace Novashot.Core.Tests;

/// <summary>
/// G-3（flameshot circlecount）：Ctrl+滚轮改编号。SetNumber 钳在 1..999。
/// </summary>
public class NumberAnnotationSetNumberTests
{
    private static NumberAnnotation Make(int n = 1) =>
        new(new SKPoint(0, 0), n, new AnnotationStyle(new SKColor(255, 0, 0), 2f));

    [Fact]
    public void SetNumber_UpdatesValue()
    {
        var ann = Make(5);
        ann.SetNumber(7);
        Assert.Equal(7, ann.Number);
    }

    [Fact]
    public void SetNumber_ClampsToRange()
    {
        var ann = Make();
        ann.SetNumber(0);
        Assert.Equal(1, ann.Number);
        ann.SetNumber(1500);
        Assert.Equal(999, ann.Number);
    }

    [Fact]
    public void Constructor_ClampsToo()
    {
        Assert.Equal(1, Make(-3).Number);
    }
}
