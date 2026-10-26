using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class FreehandToolTests
{
    private static readonly AnnotationStyle Style = new(new SKColor(255, 0, 0), 2f);

    [Fact]
    public void Click_without_move_commits_nothing()
    {
        var tool = new PencilTool();
        tool.OnPointerDown(new SKPoint(30, 30), Style);
        Assert.Null(tool.OnPointerUp(new SKPoint(30, 30)));
    }

    [Fact]
    public void Sub_step_movement_records_no_extra_points()
    {
        var tool = new PencilTool();
        tool.OnPointerDown(new SKPoint(30, 30), Style);
        // 两次均 < MinStep(1.5) 的移动：不应追加
        tool.OnPointerMove(new SKPoint(31, 30));
        tool.OnPointerMove(new SKPoint(31.4f, 30));
        var preview = Assert.IsType<PencilAnnotation>(tool.Preview);
        Assert.Single(preview.Points);
    }

    [Fact]
    public void Long_stroke_commits_annotation_with_points()
    {
        var tool = new PencilTool();
        tool.OnPointerDown(new SKPoint(0, 0), Style);
        tool.OnPointerMove(new SKPoint(10, 0));
        tool.OnPointerMove(new SKPoint(20, 0));
        var committed = tool.OnPointerUp(new SKPoint(30, 0));

        var pencil = Assert.IsType<PencilAnnotation>(committed);
        Assert.True(pencil.Points.Count >= 2);
        Assert.Equal(new SKPoint(0, 0), pencil.Points[0]);
        Assert.Equal(new SKPoint(30, 0), pencil.Points[^1]);
    }

    [Fact]
    public void Highlighter_tool_commits_highlighter_annotation()
    {
        var tool = new HighlighterTool();
        tool.OnPointerDown(new SKPoint(0, 0), Style);
        tool.OnPointerMove(new SKPoint(0, 10));
        tool.OnPointerMove(new SKPoint(0, 20));
        Assert.IsType<HighlighterAnnotation>(tool.OnPointerUp(new SKPoint(0, 25)));
    }

    [Fact]
    public void Preview_is_same_instance_as_committed_annotation()
    {
        var tool = new PencilTool();
        tool.OnPointerDown(new SKPoint(0, 0), Style);
        tool.OnPointerMove(new SKPoint(10, 0));
        var preview = tool.Preview;
        var committed = tool.OnPointerUp(new SKPoint(20, 0));
        Assert.Same(preview, committed);
    }

    [Fact]
    public void Pointer_up_appends_tail_beyond_min_step()
    {
        var tool = new PencilTool();
        tool.OnPointerDown(new SKPoint(0, 0), Style);
        tool.OnPointerMove(new SKPoint(10, 0));
        // 抬手位置距最后记录点 10px，应补记为终点
        var committed = tool.OnPointerUp(new SKPoint(20, 0));
        Assert.Equal(new SKPoint(20, 0), ((PencilAnnotation)committed!).Points[^1]);
    }

    [Fact]
    public void Cancel_clears_preview()
    {
        var tool = new HighlighterTool();
        tool.OnPointerDown(new SKPoint(0, 0), Style);
        tool.OnPointerMove(new SKPoint(10, 0));
        tool.Cancel();
        Assert.Null(tool.Preview);
        // Cancel 后再 up 也不应产出
        Assert.Null(tool.OnPointerUp(new SKPoint(20, 0)));
    }
}
