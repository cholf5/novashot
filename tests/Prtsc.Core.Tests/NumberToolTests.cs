using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class NumberToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(0, 0, 0), 2f);

    [Fact]
    public void Null_provider_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NumberTool(null!));
    }

    [Fact]
    public void OnPointerUp_uses_provider_number()
    {
        int fromProvider = 42;
        var tool = new NumberTool(() => fromProvider);
        tool.OnPointerDown(new SKPoint(10, 10), Style());
        var committed = tool.OnPointerUp(new SKPoint(10, 10));
        var na = Assert.IsType<NumberAnnotation>(committed);
        Assert.Equal(42, na.Number);
    }

    [Fact]
    public void Provider_is_called_lazily_reflecting_changes_between_gestures()
    {
        int nextValue = 1;
        var tool = new NumberTool(() => nextValue);

        tool.OnPointerDown(new SKPoint(0, 0), Style());
        var first = Assert.IsType<NumberAnnotation>(tool.OnPointerUp(new SKPoint(0, 0)));
        Assert.Equal(1, first.Number);

        nextValue = 5;  // 模拟"删除中间号"后 provider 会返回 max+1 = 5

        tool.OnPointerDown(new SKPoint(0, 0), Style());
        var second = Assert.IsType<NumberAnnotation>(tool.OnPointerUp(new SKPoint(0, 0)));
        Assert.Equal(5, second.Number);
    }

    [Fact]
    public void Preview_uses_provider_number()
    {
        var tool = new NumberTool(() => 7);
        tool.OnPointerDown(new SKPoint(0, 0), Style());
        var preview = Assert.IsType<NumberAnnotation>(tool.Preview);
        Assert.Equal(7, preview.Number);
    }

    [Fact]
    public void Cancel_produces_no_annotation()
    {
        var tool = new NumberTool(() => 1);
        tool.OnPointerDown(new SKPoint(0, 0), Style());
        tool.Cancel();
        Assert.Null(tool.Preview);
    }
}
