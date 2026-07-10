using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// 各具体标注的 <see cref="AnnotationBase.GetHandles"/> / <see cref="AnnotationBase.MoveHandle"/>
/// / <see cref="AnnotationBase.MoveBy"/> 语义测试。
/// </summary>
public class AnnotationHandleTests
{
    private static AnnotationStyle Style(float w = 2f) => new(new SKColor(255, 0, 0), w);

    [Fact]
    public void Rectangle_move_handle_updates_corresponding_edge()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        a.MoveHandle(HandleKind.BottomRight, new SKPoint(50, 40));
        Assert.Equal(new SKRect(10, 10, 50, 40), a.Rect);
    }

    [Fact]
    public void Rectangle_move_by_translates()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 30, 30), Style());
        a.MoveBy(new SKPoint(5, -3));
        Assert.Equal(new SKRect(15, 7, 35, 27), a.Rect);
    }

    [Fact]
    public void Ellipse_move_handle_uses_box_topology()
    {
        var a = new EllipseAnnotation(new SKRect(10, 10, 30, 30), Style());
        a.MoveHandle(HandleKind.TopLeft, new SKPoint(0, 0));
        Assert.Equal(new SKRect(0, 0, 30, 30), a.Rect);
    }

    [Fact]
    public void Spotlight_move_handle_updates_region_only()
    {
        var a = new SpotlightAnnotation(new SKRect(100, 100, 200, 200), 1920, 1080, Style());
        a.MoveHandle(HandleKind.Right, new SKPoint(300, 0));
        Assert.Equal(new SKRect(100, 100, 300, 200), a.Region);
        // 图像尺寸不变
        Assert.Equal(1920, a.ImageWidth);
        Assert.Equal(1080, a.ImageHeight);
    }

    [Fact]
    public void Line_has_two_endpoint_handles()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 50), Style());
        var handles = a.GetHandles();
        Assert.Equal(2, handles.Count);
        Assert.Equal(HandleKind.Start, handles[0].Kind);
        Assert.Equal(new SKPoint(10, 10), handles[0].ImagePos);
        Assert.Equal(HandleKind.End, handles[1].Kind);
        Assert.Equal(new SKPoint(50, 50), handles[1].ImagePos);
    }

    [Fact]
    public void Line_move_handle_updates_endpoint()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 50), Style());
        a.MoveHandle(HandleKind.Start, new SKPoint(0, 0));
        Assert.Equal(new SKPoint(0, 0), a.Start);
        Assert.Equal(new SKPoint(50, 50), a.End);
    }

    [Fact]
    public void Line_move_by_translates_both_endpoints()
    {
        var a = new LineAnnotation(new SKPoint(10, 10), new SKPoint(50, 50), Style());
        a.MoveBy(new SKPoint(3, 4));
        Assert.Equal(new SKPoint(13, 14), a.Start);
        Assert.Equal(new SKPoint(53, 54), a.End);
    }

    [Fact]
    public void ArrowStraight_inherits_line_handles()
    {
        var a = new ArrowStraightAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        var handles = a.GetHandles();
        Assert.Equal(2, handles.Count);
        Assert.Equal(HandleKind.Start, handles[0].Kind);
        Assert.Equal(HandleKind.End, handles[1].Kind);
    }

    [Fact]
    public void ArrowCurved_has_three_handles_start_end_control()
    {
        // M4d 起：曲箭头暴露第 3 个手柄 (Control)，用于手动调整贝塞尔控制点。
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        var handles = a.GetHandles();
        Assert.Equal(3, handles.Count);
        Assert.Contains(handles, h => h.Kind == HandleKind.Start);
        Assert.Contains(handles, h => h.Kind == HandleKind.End);
        Assert.Contains(handles, h => h.Kind == HandleKind.Control);
    }

    [Fact]
    public void Number_has_center_handle()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 3, Style(), radius: 10f);
        var handles = a.GetHandles();
        Assert.Single(handles);
        Assert.Equal(HandleKind.Center, handles[0].Kind);
        Assert.Equal(new SKPoint(50, 50), handles[0].ImagePos);
    }

    [Fact]
    public void Number_move_handle_updates_center()
    {
        var a = new NumberAnnotation(new SKPoint(50, 50), 3, Style(), radius: 10f);
        a.MoveHandle(HandleKind.Center, new SKPoint(100, 200));
        Assert.Equal(new SKPoint(100, 200), a.Center);
    }

    [Fact]
    public void Text_has_center_handle_at_bounding_box_midpoint()
    {
        var a = new TextAnnotation(new SKPoint(100, 200), "Hello", 24f, Style());
        var handles = a.GetHandles();
        Assert.Single(handles);
        Assert.Equal(HandleKind.Center, handles[0].Kind);
        var b = a.BoundingBox;
        Assert.Equal((b.Left + b.Right) * 0.5f, handles[0].ImagePos.X, 3);
        Assert.Equal((b.Top + b.Bottom) * 0.5f, handles[0].ImagePos.Y, 3);
    }

    [Fact]
    public void Text_move_handle_translates_by_delta_from_current_center()
    {
        var a = new TextAnnotation(new SKPoint(100, 200), "Hello", 24f, Style());
        var initialCenter = a.GetHandles()[0].ImagePos;
        a.MoveHandle(HandleKind.Center, new SKPoint(initialCenter.X + 20, initialCenter.Y - 5));
        var newCenter = a.GetHandles()[0].ImagePos;
        Assert.Equal(initialCenter.X + 20, newCenter.X, 3);
        Assert.Equal(initialCenter.Y - 5, newCenter.Y, 3);
    }

    [Fact]
    public void MoveBy_zero_is_no_op()
    {
        var a = new RectangleAnnotation(new SKRect(10, 10, 20, 20), Style());
        var before = a.Rect;
        a.MoveBy(SKPoint.Empty);
        Assert.Equal(before, a.Rect);
    }
}
