using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>Insert 的行为契约以及 Insert/IndexOf 与 RemoveAnnotationCommand 的协作。</summary>
public class AnnotationDocumentInsertTests
{
    private static SKBitmap NewBitmap(int w = 100, int h = 100) => new(w, h);
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);
    private static RectangleAnnotation NewRect() =>
        new(new SKRect(0, 0, 10, 10), Style());

    [Fact]
    public void Insert_places_annotation_at_index_and_fires_changed()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = NewRect();
        var b = NewRect();
        var c = NewRect();
        doc.Add(a);
        doc.Add(c);

        int fired = 0;
        doc.Changed += (_, _) => fired++;

        doc.Insert(1, b);
        Assert.Equal(3, doc.Annotations.Count);
        Assert.Same(a, doc.Annotations[0]);
        Assert.Same(b, doc.Annotations[1]);
        Assert.Same(c, doc.Annotations[2]);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Insert_at_end_is_allowed()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = NewRect();
        doc.Add(a);
        var b = NewRect();
        doc.Insert(1, b);
        Assert.Same(b, doc.Annotations[1]);
    }

    [Fact]
    public void Insert_out_of_range_throws()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Insert(-1, NewRect()));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Insert(1, NewRect())); // 空文档
    }

    [Fact]
    public void IndexOf_returns_zero_based_position()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = NewRect();
        var b = NewRect();
        doc.Add(a);
        doc.Add(b);
        Assert.Equal(0, doc.IndexOf(a));
        Assert.Equal(1, doc.IndexOf(b));
        Assert.Equal(-1, doc.IndexOf(NewRect()));
    }
}
