using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class BoxHandlesTests
{
    [Fact]
    public void GetHandles_returns_eight_in_expected_order()
    {
        var handles = BoxHandles.GetHandles(new SKRect(10, 20, 30, 40));
        Assert.Equal(8, handles.Length);
        Assert.Equal(HandleKind.TopLeft,     handles[0].Kind);
        Assert.Equal(HandleKind.Top,         handles[1].Kind);
        Assert.Equal(HandleKind.TopRight,    handles[2].Kind);
        Assert.Equal(HandleKind.Right,       handles[3].Kind);
        Assert.Equal(HandleKind.BottomRight, handles[4].Kind);
        Assert.Equal(HandleKind.Bottom,      handles[5].Kind);
        Assert.Equal(HandleKind.BottomLeft,  handles[6].Kind);
        Assert.Equal(HandleKind.Left,        handles[7].Kind);
    }

    [Fact]
    public void GetHandles_positions_are_corner_and_edge_midpoints()
    {
        var handles = BoxHandles.GetHandles(new SKRect(10, 20, 30, 40));
        Assert.Equal(new SKPoint(10, 20), handles[0].ImagePos); // TL
        Assert.Equal(new SKPoint(20, 20), handles[1].ImagePos); // T (mid X)
        Assert.Equal(new SKPoint(30, 20), handles[2].ImagePos); // TR
        Assert.Equal(new SKPoint(30, 30), handles[3].ImagePos); // R (mid Y)
        Assert.Equal(new SKPoint(30, 40), handles[4].ImagePos); // BR
        Assert.Equal(new SKPoint(20, 40), handles[5].ImagePos); // B
        Assert.Equal(new SKPoint(10, 40), handles[6].ImagePos); // BL
        Assert.Equal(new SKPoint(10, 30), handles[7].ImagePos); // L
    }

    [Fact]
    public void GetHandles_standardizes_negative_rect()
    {
        // 传入 right<left 的矩形，handles 应基于标准化后的矩形
        var handles = BoxHandles.GetHandles(new SKRect(30, 40, 10, 20));
        Assert.Equal(new SKPoint(10, 20), handles[0].ImagePos);
        Assert.Equal(new SKPoint(30, 40), handles[4].ImagePos);
    }

    [Fact]
    public void ApplyHandle_TopLeft_updates_left_and_top()
    {
        var r = BoxHandles.ApplyHandle(new SKRect(10, 20, 30, 40), HandleKind.TopLeft, new SKPoint(15, 22));
        Assert.Equal(new SKRect(15, 22, 30, 40), r);
    }

    [Fact]
    public void ApplyHandle_Top_only_updates_top()
    {
        var r = BoxHandles.ApplyHandle(new SKRect(10, 20, 30, 40), HandleKind.Top, new SKPoint(0, 25));
        Assert.Equal(new SKRect(10, 25, 30, 40), r);
    }

    [Fact]
    public void ApplyHandle_Right_only_updates_right()
    {
        var r = BoxHandles.ApplyHandle(new SKRect(10, 20, 30, 40), HandleKind.Right, new SKPoint(35, 0));
        Assert.Equal(new SKRect(10, 20, 35, 40), r);
    }

    [Fact]
    public void ApplyHandle_BottomLeft_updates_left_and_bottom()
    {
        var r = BoxHandles.ApplyHandle(new SKRect(10, 20, 30, 40), HandleKind.BottomLeft, new SKPoint(5, 50));
        Assert.Equal(new SKRect(5, 20, 30, 50), r);
    }

    [Fact]
    public void ApplyHandle_can_produce_negative_rect_before_standardize()
    {
        // Right < Left：允许"越过"，调用方负责 Standardize
        var r = BoxHandles.ApplyHandle(new SKRect(10, 20, 30, 40), HandleKind.Right, new SKPoint(5, 0));
        Assert.Equal(new SKRect(10, 20, 5, 40), r);
        var std = r.Standardized;
        Assert.Equal(new SKRect(5, 20, 10, 40), std);
    }

    [Fact]
    public void ApplyHandle_ignores_non_box_kinds()
    {
        var src = new SKRect(10, 20, 30, 40);
        Assert.Equal(src, BoxHandles.ApplyHandle(src, HandleKind.Start,  new SKPoint(0, 0)));
        Assert.Equal(src, BoxHandles.ApplyHandle(src, HandleKind.End,    new SKPoint(0, 0)));
        Assert.Equal(src, BoxHandles.ApplyHandle(src, HandleKind.Center, new SKPoint(0, 0)));
    }
}
