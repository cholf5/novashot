using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>SelectTool 状态机的行为契约。</summary>
public class SelectToolTests
{
    private static AnnotationStyle Style() => new(new SKColor(255, 0, 0), 2f);

    private sealed class Harness
    {
        public AnnotationBase? Selection { get; set; }
        public UndoRedoStack History { get; } = new();
        public List<AnnotationBase> Annotations { get; } = new();

        public SelectTool Tool { get; }

        public Harness(float bodySlop = 4f, float handleRadius = 8f)
        {
            Tool = new SelectTool(
                getSelection: () => Selection,
                setSelection: a => Selection = a,
                history: History,
                annotations: Annotations,
                bodyHitSlop: bodySlop,
                handleHitRadius: handleRadius);
        }
    }

    [Fact]
    public void Click_on_body_selects_annotation()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 50, 50), Style());
        h.Annotations.Add(rect);

        h.Tool.OnPointerDown(new SKPoint(30, 30), Style());
        Assert.Same(rect, h.Selection);
    }

    [Fact]
    public void Click_on_empty_area_deselects()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 50, 50), Style());
        h.Annotations.Add(rect);
        h.Selection = rect;

        h.Tool.OnPointerDown(new SKPoint(200, 200), Style());
        Assert.Null(h.Selection);
    }

    [Fact]
    public void Topmost_annotation_wins_when_overlapping()
    {
        var h = new Harness();
        var below = new RectangleAnnotation(new SKRect(0, 0, 100, 100), Style());
        var above = new RectangleAnnotation(new SKRect(10, 10, 50, 50), Style());
        h.Annotations.Add(below);
        h.Annotations.Add(above);

        h.Tool.OnPointerDown(new SKPoint(20, 20), Style());
        Assert.Same(above, h.Selection);
    }

    [Fact]
    public void Drag_body_moves_annotation_and_pushes_MoveCommand_on_release()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        h.Annotations.Add(rect);

        h.Tool.OnPointerDown(new SKPoint(20, 20), Style());
        h.Tool.OnPointerMove(new SKPoint(25, 22));
        h.Tool.OnPointerMove(new SKPoint(30, 25));
        h.Tool.OnPointerUp(new SKPoint(30, 25));

        // 位置：从 (10,10)-(30,30) 平移 (10,5) 到 (20,15)-(40,35)
        Assert.Equal(new SKRect(20, 15, 40, 35), rect.Rect);
        Assert.True(h.History.CanUndo);

        h.History.Undo();
        Assert.Equal(new SKRect(10, 10, 30, 30), rect.Rect);
    }

    [Fact]
    public void Zero_move_does_not_push_command()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        h.Annotations.Add(rect);

        h.Tool.OnPointerDown(new SKPoint(20, 20), Style());
        h.Tool.OnPointerUp(new SKPoint(20, 20));

        Assert.False(h.History.CanUndo);
    }

    [Fact]
    public void Drag_handle_resizes_and_pushes_ResizeCommand_on_release()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        h.Annotations.Add(rect);
        h.Selection = rect;

        // 拖 BottomRight handle (位置 30,30) 到 (60, 50)
        h.Tool.OnPointerDown(new SKPoint(30, 30), Style());
        h.Tool.OnPointerMove(new SKPoint(45, 40));
        h.Tool.OnPointerUp(new SKPoint(60, 50));

        Assert.Equal(new SKRect(10, 10, 60, 50), rect.Rect);
        Assert.True(h.History.CanUndo);

        h.History.Undo();
        Assert.Equal(new SKRect(10, 10, 30, 30), rect.Rect);
    }

    [Fact]
    public void Handle_hit_takes_priority_over_body()
    {
        var h = new Harness();
        // 大矩形，body 覆盖 (0,0)-(100,100)；小 selection 在中心
        var big = new RectangleAnnotation(new SKRect(0, 0, 100, 100), Style());
        var sel = new RectangleAnnotation(new SKRect(40, 40, 60, 60), Style());
        h.Annotations.Add(big);
        h.Annotations.Add(sel);
        h.Selection = sel;

        // 点在 sel 的 BottomRight handle 附近（60,60），也在 big 的 body 内
        h.Tool.OnPointerDown(new SKPoint(60, 60), Style());
        Assert.Same(sel, h.Selection); // selection 未变

        h.Tool.OnPointerMove(new SKPoint(80, 80));
        h.Tool.OnPointerUp(new SKPoint(80, 80));

        // sel 被 resize 到 (40,40)-(80,80)
        Assert.Equal(new SKRect(40, 40, 80, 80), sel.Rect);
    }

    [Fact]
    public void Cancel_reverts_move()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        h.Annotations.Add(rect);

        h.Tool.OnPointerDown(new SKPoint(20, 20), Style());
        h.Tool.OnPointerMove(new SKPoint(50, 50));
        h.Tool.Cancel();

        Assert.Equal(new SKRect(10, 10, 30, 30), rect.Rect);
        Assert.False(h.History.CanUndo);
    }

    [Fact]
    public void Cancel_reverts_resize()
    {
        var h = new Harness();
        var rect = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        h.Annotations.Add(rect);
        h.Selection = rect;

        h.Tool.OnPointerDown(new SKPoint(30, 30), Style()); // BottomRight handle
        h.Tool.OnPointerMove(new SKPoint(50, 50));
        h.Tool.Cancel();

        Assert.Equal(new SKRect(10, 10, 30, 30), rect.Rect);
        Assert.False(h.History.CanUndo);
    }

    [Fact]
    public void Preview_is_always_null()
    {
        var h = new Harness();
        Assert.Null(h.Tool.Preview);
        h.Tool.OnPointerDown(new SKPoint(0, 0), Style());
        Assert.Null(h.Tool.Preview);
    }
}
