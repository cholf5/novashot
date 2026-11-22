using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class ArrowCurvedControlHandleTests
{
    private static AnnotationStyle Style(float w = 2f) =>
        new(new SKColor(255, 0, 0), w);

    [Fact]
    public void Default_has_no_custom_control_point()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        Assert.False(a.HasCustomControlPoint);
    }

    [Fact]
    public void GetHandles_returns_three_handles_start_end_control()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        var handles = a.GetHandles();
        Assert.Equal(3, handles.Count);
        Assert.Contains(handles, h => h.Kind == HandleKind.Start);
        Assert.Contains(handles, h => h.Kind == HandleKind.End);
        Assert.Contains(handles, h => h.Kind == HandleKind.Control);
    }

    [Fact]
    public void Control_handle_position_matches_derived_control_point_by_default()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        var derived = a.ControlPoint;
        var ctrlHandle = a.GetHandles().Single(h => h.Kind == HandleKind.Control);
        Assert.Equal(derived, ctrlHandle.ImagePos);
    }

    [Fact]
    public void MoveHandle_Control_sets_absolute_and_marks_custom()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        a.MoveHandle(HandleKind.Control, new SKPoint(30, -20));
        Assert.True(a.HasCustomControlPoint);
        Assert.Equal(new SKPoint(30, -20), a.ControlPoint);
    }

    [Fact]
    public void Setting_ControlPoint_property_marks_custom()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        Assert.False(a.HasCustomControlPoint);
        a.ControlPoint = new SKPoint(60, 40);
        Assert.True(a.HasCustomControlPoint);
        Assert.Equal(new SKPoint(60, 40), a.ControlPoint);
    }

    [Fact]
    public void ResetControlPoint_returns_to_derived()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        a.ControlPoint = new SKPoint(0, 0);
        Assert.True(a.HasCustomControlPoint);
        a.ResetControlPoint();
        Assert.False(a.HasCustomControlPoint);
        // derived: (50, 18)
        Assert.Equal(50f, a.ControlPoint.X, 3);
        Assert.Equal(18f, a.ControlPoint.Y, 3);
    }

    [Fact]
    public void MoveHandle_StartOrEnd_keeps_custom_control_absolute()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        a.MoveHandle(HandleKind.Control, new SKPoint(30, -20));
        a.MoveHandle(HandleKind.Start, new SKPoint(-50, 0));  // 拉远起点
        // 控制点保持绝对位置
        Assert.Equal(new SKPoint(30, -20), a.ControlPoint);
        Assert.True(a.HasCustomControlPoint);
    }

    [Fact]
    public void MoveBy_translates_custom_control_together_with_endpoints()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        a.MoveHandle(HandleKind.Control, new SKPoint(30, -20));
        a.MoveBy(new SKPoint(10, 5));
        Assert.Equal(new SKPoint(10, 5), a.Start);
        Assert.Equal(new SKPoint(110, 5), a.End);
        Assert.Equal(new SKPoint(40, -15), a.ControlPoint);
        Assert.True(a.HasCustomControlPoint);
    }

    [Fact]
    public void MoveBy_without_custom_control_stays_derived()
    {
        var a = new ArrowCurvedAnnotation(new SKPoint(0, 0), new SKPoint(100, 0), Style());
        Assert.False(a.HasCustomControlPoint);
        a.MoveBy(new SKPoint(10, 5));
        Assert.False(a.HasCustomControlPoint);
        // 推导：中点 (60, 5)，法向 (0, 1)，偏移 18 → (60, 23)
        Assert.Equal(60f, a.ControlPoint.X, 3);
        Assert.Equal(23f, a.ControlPoint.Y, 3);
    }
}
