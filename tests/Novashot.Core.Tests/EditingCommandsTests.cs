using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class RemoveAnnotationCommandTests
{
    private static SKBitmap NewBitmap() => new(100, 100);
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);
    private static RectangleAnnotation NewRect(float x = 0) =>
        new(new SKRect(x, 0, x + 10, 10), Style());

    [Fact]
    public void Do_removes_and_undo_restores_at_original_index()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = NewRect(0);
        var b = NewRect(20);
        var c = NewRect(40);
        doc.Add(a); doc.Add(b); doc.Add(c);

        var cmd = new RemoveAnnotationCommand(doc, b);
        cmd.Do();
        Assert.Equal(2, doc.Annotations.Count);
        Assert.Same(a, doc.Annotations[0]);
        Assert.Same(c, doc.Annotations[1]);

        cmd.Undo();
        Assert.Equal(3, doc.Annotations.Count);
        Assert.Same(a, doc.Annotations[0]);
        Assert.Same(b, doc.Annotations[1]); // 恢复到原来的 index=1
        Assert.Same(c, doc.Annotations[2]);
    }

    [Fact]
    public void Constructor_throws_when_annotation_not_in_document()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var orphan = NewRect();
        Assert.Throws<ArgumentException>(() => new RemoveAnnotationCommand(doc, orphan));
    }

    [Fact]
    public void Description_names_annotation_type()
    {
        using var bmp = NewBitmap();
        var doc = new AnnotationDocument(bmp);
        var a = NewRect();
        doc.Add(a);
        var cmd = new RemoveAnnotationCommand(doc, a);
        Assert.Contains(nameof(RectangleAnnotation), cmd.Description);
    }
}

public class MoveAnnotationCommandTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Rectangle_do_undo_is_symmetric()
    {
        var a = new RectangleAnnotation(new SKRect(10, 20, 30, 40), Style());
        var before = a.Rect;
        var cmd = new MoveAnnotationCommand(a, new SKPoint(7, -3));
        cmd.Do();
        Assert.Equal(new SKRect(17, 17, 37, 37), a.Rect);
        cmd.Undo();
        Assert.Equal(before, a.Rect);
    }

    [Fact]
    public void Line_do_undo_is_symmetric()
    {
        var a = new LineAnnotation(new SKPoint(10, 20), new SKPoint(30, 40), Style());
        var beforeStart = a.Start;
        var beforeEnd = a.End;
        var cmd = new MoveAnnotationCommand(a, new SKPoint(5, 5));
        cmd.Do();
        cmd.Undo();
        Assert.Equal(beforeStart, a.Start);
        Assert.Equal(beforeEnd, a.End);
    }

    [Fact]
    public void Number_do_undo_is_symmetric()
    {
        var a = new NumberAnnotation(new SKPoint(100, 200), 1, Style(), radius: 10f);
        var cmd = new MoveAnnotationCommand(a, new SKPoint(-20, 30));
        cmd.Do();
        Assert.Equal(new SKPoint(80, 230), a.Center);
        cmd.Undo();
        Assert.Equal(new SKPoint(100, 200), a.Center);
    }
}

public class ResizeAnnotationCommandTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Rectangle_resize_do_undo_is_symmetric()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        var cmd = new ResizeAnnotationCommand(a, HandleKind.BottomRight,
            before: new SKPoint(30, 30), after: new SKPoint(50, 40));
        cmd.Do();
        Assert.Equal(new SKRect(10, 10, 50, 40), a.Rect);
        cmd.Undo();
        Assert.Equal(new SKRect(10, 10, 30, 30), a.Rect);
    }

    [Fact]
    public void Line_resize_start_do_undo_is_symmetric()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 50), Style());
        var cmd = new ResizeAnnotationCommand(a, HandleKind.Start,
            before: new SKPoint(10, 10), after: new SKPoint(0, 0));
        cmd.Do();
        Assert.Equal(new SKPoint(0, 0), a.Start);
        cmd.Undo();
        Assert.Equal(new SKPoint(10, 10), a.Start);
    }

    [Fact]
    public void Redo_after_undo_reproduces_after_state()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        var cmd = new ResizeAnnotationCommand(a, HandleKind.TopLeft,
            before: new SKPoint(10, 10), after: new SKPoint(5, 5));
        cmd.Do();
        cmd.Undo();
        cmd.Do(); // redo
        Assert.Equal(new SKRect(5, 5, 30, 30), a.Rect);
    }
}
