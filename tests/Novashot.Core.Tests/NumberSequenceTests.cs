using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class NumberSequenceTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    private static NumberAnnotation Num(int n) =>
        new(new SKPoint(0, 0), n, Style());

    [Fact]
    public void Empty_list_returns_one()
    {
        Assert.Equal(1, NumberSequence.NextFor(Array.Empty<AnnotationBase>()));
    }

    [Fact]
    public void Returns_max_plus_one_when_all_are_numbers()
    {
        var list = new AnnotationBase[] { Num(1), Num(2), Num(3) };
        Assert.Equal(4, NumberSequence.NextFor(list));
    }

    [Fact]
    public void Ignores_non_number_annotations()
    {
        var rect = new RectangleAnnotation(new SKRect(0, 0, 10, 10), Style());
        var line = new LineAnnotation(new SKPoint(0, 0), new SKPoint(10, 10), Style());
        var list = new AnnotationBase[] { rect, Num(1), line, Num(5) };
        Assert.Equal(6, NumberSequence.NextFor(list));
    }

    [Fact]
    public void Gap_in_middle_still_returns_max_plus_one()
    {
        // 用户删除了 #2，剩下 1 和 3 —— 下一个新号是 4，不是 2。
        var list = new AnnotationBase[] { Num(1), Num(3) };
        Assert.Equal(4, NumberSequence.NextFor(list));
    }

    [Fact]
    public void Top_number_removed_recovers_slot()
    {
        // 落到 3 后 Undo 掉 #3，剩 1、2 —— 下一个是 3（天然回收）。
        var list = new AnnotationBase[] { Num(1), Num(2) };
        Assert.Equal(3, NumberSequence.NextFor(list));
    }

    [Fact]
    public void Order_independent()
    {
        var list = new AnnotationBase[] { Num(7), Num(3), Num(5) };
        Assert.Equal(8, NumberSequence.NextFor(list));
    }

    [Fact]
    public void Null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => NumberSequence.NextFor(null!));
    }
}
