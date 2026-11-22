using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class AnnotationDocumentTests
{
    private static SKBitmap MakeBitmap(int w = 4, int h = 4) => new(w, h);

    private static RectangleAnnotation MakeRect() =>
        new(new SKRect(0, 0, 10, 10), new AnnotationStyle(new SKColor(255, 0, 0), 1f));

    [Fact]
    public void Add_appends_and_fires_Changed()
    {
        using var bmp = MakeBitmap();
        var doc = new AnnotationDocument(bmp);
        var fired = 0;
        doc.Changed += (_, _) => fired++;

        var a = MakeRect();
        doc.Add(a);

        Assert.Equal(1, fired);
        Assert.Single(doc.Annotations);
        Assert.Same(a, doc.Annotations[0]);
    }

    [Fact]
    public void Remove_returns_true_and_fires_when_present()
    {
        using var bmp = MakeBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = MakeRect();
        doc.Add(a);

        var fired = 0;
        doc.Changed += (_, _) => fired++;

        Assert.True(doc.Remove(a));
        Assert.Equal(1, fired);
        Assert.Empty(doc.Annotations);
    }

    [Fact]
    public void Remove_returns_false_without_firing_when_absent()
    {
        using var bmp = MakeBitmap();
        var doc = new AnnotationDocument(bmp);
        var fired = 0;
        doc.Changed += (_, _) => fired++;

        Assert.False(doc.Remove(MakeRect()));
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Clear_empties_and_fires_once()
    {
        using var bmp = MakeBitmap();
        var doc = new AnnotationDocument(bmp);
        doc.Add(MakeRect());
        doc.Add(MakeRect());
        var fired = 0;
        doc.Changed += (_, _) => fired++;

        doc.Clear();
        Assert.Empty(doc.Annotations);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_on_empty_document_does_not_fire()
    {
        using var bmp = MakeBitmap();
        var doc = new AnnotationDocument(bmp);
        var fired = 0;
        doc.Changed += (_, _) => fired++;

        doc.Clear();
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Width_Height_reflect_source_bitmap()
    {
        using var bmp = MakeBitmap(320, 200);
        var doc = new AnnotationDocument(bmp);
        Assert.Equal(320, doc.Width);
        Assert.Equal(200, doc.Height);
    }
}
