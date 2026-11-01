using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class NumberSequenceMinUnusedTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    private static NumberAnnotation MakeNumber(int n) => new(new SKPoint(0, 0), n, Style());

    [Fact]
    public void EmptyDocument_StartsAtOne() =>
        Assert.Equal(1, NumberSequenceMinUnused.NextFor([]));

    [Fact]
    public void ContiguousNumbers_NextIsCountPlusOne()
    {
        var doc = new List<AnnotationBase> { MakeNumber(1), MakeNumber(2), MakeNumber(3) };
        Assert.Equal(4, NumberSequenceMinUnused.NextFor(doc));
    }

    [Fact]
    public void DeletedMiddle_ReusesSmallestUnused()
    {
        // 1,2,4 → 最小未用是 3（winshot 语义：删号后回收）
        var doc = new List<AnnotationBase> { MakeNumber(1), MakeNumber(2), MakeNumber(4) };
        Assert.Equal(3, NumberSequenceMinUnused.NextFor(doc));
    }

    [Fact]
    public void GapAtStart_ReusesOne()
    {
        var doc = new List<AnnotationBase> { MakeNumber(2), MakeNumber(3) };
        Assert.Equal(1, NumberSequenceMinUnused.NextFor(doc));
    }

    [Fact]
    public void NonNumberAnnotationsIgnored()
    {
        var rect = new RectangleAnnotation(new SKRect(0, 0, 10, 10), Style());
        var doc = new List<AnnotationBase> { rect, MakeNumber(5) };
        Assert.Equal(1, NumberSequenceMinUnused.NextFor(doc));
    }

    [Fact]
    public void Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => NumberSequenceMinUnused.NextFor(null!));
}
