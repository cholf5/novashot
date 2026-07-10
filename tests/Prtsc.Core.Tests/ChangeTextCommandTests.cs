using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.History;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class ChangeTextCommandTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);
    private static TextAnnotation NewText(string s = "hello")
        => new(new SKPoint(50, 60), s, 24f, Style());

    [Fact]
    public void Do_replaces_text()
    {
        var a = NewText("before");
        var cmd = new ChangeTextCommand(a, "before", "after");
        cmd.Do();
        Assert.Equal("after", a.Text);
    }

    [Fact]
    public void Undo_restores_text()
    {
        var a = NewText("before");
        var cmd = new ChangeTextCommand(a, "before", "after");
        cmd.Do();
        cmd.Undo();
        Assert.Equal("before", a.Text);
    }

    [Fact]
    public void Do_Undo_are_repeatable()
    {
        var a = NewText("x");
        var cmd = new ChangeTextCommand(a, "x", "y");
        cmd.Do(); cmd.Undo(); cmd.Do(); cmd.Undo();
        Assert.Equal("x", a.Text);
        cmd.Do();
        Assert.Equal("y", a.Text);
    }

    [Fact]
    public void Null_before_and_after_are_treated_as_empty()
    {
        var a = NewText("x");
        var cmd = new ChangeTextCommand(a, null!, null!);
        cmd.Do();
        Assert.Equal(string.Empty, a.Text);
        cmd.Undo();
        Assert.Equal(string.Empty, a.Text);
    }

    [Fact]
    public void Description_names_edit_text()
    {
        var a = NewText();
        var cmd = new ChangeTextCommand(a, "a", "b");
        Assert.Equal("Edit Text", cmd.Description);
    }

    [Fact]
    public void Do_and_Undo_notify_document_when_provided()
    {
        using var bmp = new SKBitmap(100, 100);
        var doc = new AnnotationDocument(bmp);
        var a = NewText();
        doc.Add(a);
        int notifications = 0;
        doc.Changed += (_, _) => notifications++;
        var cmd = new ChangeTextCommand(a, "hello", "world", doc);
        cmd.Do();
        cmd.Undo();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Do_and_Undo_are_silent_without_document()
    {
        var a = NewText();
        var cmd = new ChangeTextCommand(a, "a", "b");
        // no document: should just work, no exception
        cmd.Do();
        cmd.Undo();
        Assert.Equal("a", a.Text);
    }
}

public class AnnotationDocumentNotifyMutatedTests
{
    [Fact]
    public void NotifyMutated_fires_Changed_event()
    {
        using var bmp = new SKBitmap(50, 50);
        var doc = new AnnotationDocument(bmp);
        int fired = 0;
        doc.Changed += (_, _) => fired++;
        doc.NotifyMutated();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void NotifyMutated_can_be_called_multiple_times()
    {
        using var bmp = new SKBitmap(50, 50);
        var doc = new AnnotationDocument(bmp);
        int fired = 0;
        doc.Changed += (_, _) => fired++;
        doc.NotifyMutated();
        doc.NotifyMutated();
        doc.NotifyMutated();
        Assert.Equal(3, fired);
    }
}

public class MoveResizeCommandMutationNotificationTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void MoveCommand_notifies_document_on_Do_and_Undo()
    {
        using var bmp = new SKBitmap(100, 100);
        var doc = new AnnotationDocument(bmp);
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        doc.Add(rect);
        int notifications = 0;
        doc.Changed += (_, _) => notifications++;

        var cmd = new Prtsc.Core.Annotation.History.MoveAnnotationCommand(
            rect, new SKPoint(5, 5), doc);
        cmd.Do();
        cmd.Undo();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void ResizeCommand_notifies_document_on_Do_and_Undo()
    {
        using var bmp = new SKBitmap(100, 100);
        var doc = new AnnotationDocument(bmp);
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        doc.Add(rect);
        int notifications = 0;
        doc.Changed += (_, _) => notifications++;

        var cmd = new Prtsc.Core.Annotation.History.ResizeAnnotationCommand(
            rect, Prtsc.Core.Annotation.Editing.HandleKind.BottomRight,
            before: new SKPoint(30, 30), after: new SKPoint(50, 40),
            document: doc);
        cmd.Do();
        cmd.Undo();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Commands_without_document_dont_throw_and_dont_notify()
    {
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        var move = new Prtsc.Core.Annotation.History.MoveAnnotationCommand(rect, new SKPoint(1, 1));
        move.Do(); move.Undo(); // no exception, no doc
        Assert.Equal(new SKRect(10, 10, 30, 30), rect.Rect);
    }
}
