using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.History;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class PixelEffectAnnotationTests : IDisposable
{
    private readonly SKBitmap _source = new(200, 100);

    private static readonly AnnotationStyle Style = AnnotationStyle.Default;

    public void Dispose() => _source.Dispose();

    [Fact]
    public void Region_is_standardized_on_construction_and_set()
    {
        var m = new MosaicAnnotation(new SKRect(50, 40, 10, 20), _source, Style);
        Assert.Equal(new SKRect(10, 20, 50, 40), m.Region);
        m.Region = new SKRect(80, 70, 30, 10);
        Assert.Equal(new SKRect(30, 10, 80, 70), m.Region);
    }

    [Fact]
    public void BoundingBox_is_region_with_1px_pad()
    {
        var b = new BlurAnnotation(new SKRect(10, 20, 60, 80), _source, Style);
        Assert.Equal(new SKRect(9, 19, 61, 81), b.BoundingBox);
    }

    [Fact]
    public void HitTest_inside_region_true_outside_false()
    {
        var m = new MosaicAnnotation(new SKRect(10, 10, 60, 60), _source, Style);
        Assert.True(m.HitTest(new SKPoint(30, 30), slop: 0f));
        Assert.False(m.HitTest(new SKPoint(5, 30), slop: 0f));
        // slop 扩张边界
        Assert.True(m.HitTest(new SKPoint(9, 30), slop: 2f));
    }

    [Fact]
    public void GetHandles_returns_eight_box_handles()
    {
        var m = new MosaicAnnotation(new SKRect(10, 10, 60, 60), _source, Style);
        Assert.Equal(8, m.GetHandles().Count);
    }

    [Fact]
    public void MoveHandle_resizes_region()
    {
        var m = new MosaicAnnotation(new SKRect(10, 10, 60, 60), _source, Style);
        m.MoveHandle(Prtsc.Core.Annotation.Editing.HandleKind.BottomRight, new SKPoint(100, 90));
        Assert.Equal(new SKRect(10, 10, 100, 90), m.Region);
    }

    [Fact]
    public void MoveBy_offsets_region()
    {
        var b = new BlurAnnotation(new SKRect(10, 10, 60, 60), _source, Style);
        b.MoveBy(new SKPoint(5, 7));
        Assert.Equal(new SKRect(15, 17, 65, 67), b.Region);
    }

    [Fact]
    public void Mosaic_BlockSize_defaults_and_clamps()
    {
        var m = new MosaicAnnotation(new SKRect(0, 0, 50, 50), _source, Style);
        Assert.Equal(MosaicAnnotation.DefaultBlockSize, m.BlockSize);
        m.BlockSize = 999;
        Assert.Equal(MosaicAnnotation.MaxBlockSize, m.BlockSize);
        m.BlockSize = 1;
        Assert.Equal(MosaicAnnotation.MinBlockSize, m.BlockSize);
    }

    [Fact]
    public void Blur_Sigma_defaults_and_clamps()
    {
        var b = new BlurAnnotation(new SKRect(0, 0, 50, 50), _source, Style);
        Assert.Equal(BlurAnnotation.DefaultSigma, b.Sigma);
        b.Sigma = 100;
        Assert.Equal(BlurAnnotation.MaxSigma, b.Sigma);
        b.Sigma = 0.5f;
        Assert.Equal(BlurAnnotation.MinSigma, b.Sigma);
    }

    [Fact]
    public void Effect_annotation_round_trips_through_undo_stack()
    {
        var doc = new AnnotationDocument(_source);
        var history = new UndoRedoStack();
        var mosaic = new MosaicAnnotation(new SKRect(10, 10, 60, 60), _source, Style);

        history.Execute(new AddAnnotationCommand(doc, mosaic));
        Assert.Single(doc.Annotations);

        history.Undo();
        Assert.Empty(doc.Annotations);

        history.Redo();
        Assert.Same(mosaic, Assert.Single(doc.Annotations));
    }
}
