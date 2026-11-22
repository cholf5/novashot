using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class RegionEffectToolTests : IDisposable
{
    private readonly SKBitmap _source = new(200, 100);
    private static readonly AnnotationStyle Style = AnnotationStyle.Default;

    private RegionEffectTool NewMosaicTool() =>
        new((rect, style) => new MosaicAnnotation(rect, _source, style));

    public void Dispose() => _source.Dispose();

    [Fact]
    public void Drag_produces_annotation_with_standardized_region()
    {
        var tool = NewMosaicTool();
        tool.OnPointerDown(new SKPoint(80, 60), Style);
        tool.OnPointerMove(new SKPoint(20, 10));
        var committed = tool.OnPointerUp(new SKPoint(20, 10));

        var mosaic = Assert.IsType<MosaicAnnotation>(committed);
        Assert.Equal(new SKRect(20, 10, 80, 60), mosaic.Region);
    }

    [Fact]
    public void Tiny_drag_commits_nothing()
    {
        var tool = NewMosaicTool();
        tool.OnPointerDown(new SKPoint(50, 50), Style);
        Assert.Null(tool.OnPointerUp(new SKPoint(52, 51))); // 2x1 px < 4px 阈值
    }

    [Fact]
    public void Preview_grows_during_drag_and_clears_on_up()
    {
        var tool = NewMosaicTool();
        tool.OnPointerDown(new SKPoint(10, 10), Style);
        Assert.NotNull(tool.Preview);
        tool.OnPointerMove(new SKPoint(60, 40));
        var preview = Assert.IsType<MosaicAnnotation>(tool.Preview);
        Assert.Equal(new SKRect(10, 10, 60, 40), preview.Region);
        tool.OnPointerUp(new SKPoint(60, 40));
        Assert.Null(tool.Preview);
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var tool = NewMosaicTool();
        tool.OnPointerDown(new SKPoint(10, 10), Style);
        tool.OnPointerMove(new SKPoint(60, 40));
        tool.Cancel();
        Assert.Null(tool.Preview);
    }
}
