using Novashot.Core.Annotation;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using SkiaSharp;
using Xunit;

namespace Novashot.Core.Tests;

/// <summary>
/// Inspector 元素列表的层序基建：AnnotationDocument.Move 与 ReorderAnnotationCommand。
/// Do/Undo 幂等（索引互换），from==to 是无事件 no-op，越界抛参数异常。
/// </summary>
public class AnnotationDocumentMoveTests
{
    private static AnnotationDocument Doc(int count)
    {
        var doc = new AnnotationDocument(new SKBitmap(10, 10));
        for (int i = 0; i < count; i++)
            doc.Add(new NumberAnnotation(new SKPoint(i, 0), i + 1, AnnotationStyle.Default));
        return doc;
    }

    [Fact]
    public void Move_MiddleToTop_RestacksOrder()
    {
        var doc = Doc(3);
        var a1 = doc.Annotations[1];
        var a2 = doc.Annotations[2];
        doc.Move(1, 2);
        Assert.Equal(2, doc.IndexOf(a1));
        // 原栈顶被挤到中位
        Assert.Equal(1, doc.IndexOf(a2));
    }

    [Fact]
    public void MoveUndo_RestoresOriginalOrder()
    {
        var doc = Doc(3);
        var a1 = doc.Annotations[1];
        var cmd = new ReorderAnnotationCommand(doc, a1, toIndex: 2);
        cmd.Do();
        Assert.Equal(2, doc.IndexOf(a1));
        cmd.Undo();
        Assert.Equal(1, doc.IndexOf(a1));
    }

    [Fact]
    public void Move_SameIndex_NoChangeNoEvent()
    {
        var doc = Doc(2);
        var fired = false;
        doc.Changed += (_, _) => fired = true;
        doc.Move(1, 1);
        Assert.False(fired);
    }

    [Fact]
    public void Move_TriggersChanged()
    {
        var doc = Doc(2);
        var fired = false;
        doc.Changed += (_, _) => fired = true;
        doc.Move(0, 1);
        Assert.True(fired);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    [InlineData(2, 0)]
    public void Move_OutOfRange_Throws(int from, int to)
    {
        var doc = Doc(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Move(from, to));
    }

    [Fact]
    public void Command_NotInDocument_Throws()
    {
        var doc = Doc(1);
        var outsider = new NumberAnnotation(new SKPoint(9, 9), 9, AnnotationStyle.Default);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReorderAnnotationCommand(doc, outsider, toIndex: 0));
    }

    [Fact]
    public void Command_ToIndexOutOfRange_Throws()
    {
        var doc = Doc(1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReorderAnnotationCommand(doc, doc.Annotations[0], toIndex: 3));
    }
}
