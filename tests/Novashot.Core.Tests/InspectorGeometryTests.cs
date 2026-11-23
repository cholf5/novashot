using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.Model;
using SkiaSharp;
using Xunit;

namespace Novashot.Core.Tests;

/// <summary>
/// Inspector 几何数值编辑的纯映射：X/Y → Move delta、宽/高 → SE 手柄目标点，
/// 以及「谁支持宽高编辑」的权限矩阵。
/// </summary>
public class InspectorGeometryTests
{
    private static RectangleAnnotation Rect(float x, float y, float w, float h)
        => new(new SKRect(x, y, x + w, y + h), AnnotationStyle.Default);

    // ---- SupportsResize：盒状族 true，其余 false ----

    [Fact]
    public void SupportsResize_BoxFamily_True()
    {
        Assert.True(InspectorGeometry.SupportsResize(Rect(0, 0, 5, 5)));
        Assert.True(InspectorGeometry.SupportsResize(
            new EllipseAnnotation(new SKRect(0, 0, 5, 5), AnnotationStyle.Default)));
        Assert.True(InspectorGeometry.SupportsResize(
            new MosaicAnnotation(new SKRect(0, 0, 5, 5), new SKBitmap(10, 10), AnnotationStyle.Default)));
        Assert.True(InspectorGeometry.SupportsResize(
            new SpotlightAnnotation(new SKRect(0, 0, 5, 5), 100, 100, AnnotationStyle.Default)));
    }

    [Fact]
    public void SupportsResize_TextNumberOpenShapes_False()
    {
        Assert.False(InspectorGeometry.SupportsResize(
            new TextAnnotation(new SKPoint(0, 0), "hi", 24f, AnnotationStyle.Default)));
        Assert.False(InspectorGeometry.SupportsResize(
            new NumberAnnotation(new SKPoint(5, 5), 1, AnnotationStyle.Default)));
        Assert.False(InspectorGeometry.SupportsResize(
            new LineAnnotation(new SKPoint(0, 0), new SKPoint(9, 9), AnnotationStyle.Default)));
    }

    // ---- MoveDelta：clamp 到画布内 ----

    [Fact]
    public void MoveDelta_FreeMove_ReturnsDelta()
    {
        var d = InspectorGeometry.MoveDelta(new SKRect(2, 2, 6, 6), 4, 5, 100, 100);
        Assert.Equal(2f, d.X);
        Assert.Equal(3f, d.Y);
    }

    [Fact]
    public void MoveDelta_OutOfCanvas_Clamps()
    {
        var d = InspectorGeometry.MoveDelta(new SKRect(2, 2, 6, 6), -5, 99, 100, 100);
        Assert.Equal(-2f, d.X); // left >= 0
        Assert.Equal(94f, d.Y); // top <= 100 - width(4) = 96
    }

    [Fact]
    public void MoveDelta_BiggerThanCanvas_SticksToOrigin()
    {
        var d = InspectorGeometry.MoveDelta(new SKRect(2, 2, 200, 200), 50, 50, 100, 100);
        Assert.Equal(-2f, d.X);
        Assert.Equal(-2f, d.Y);
    }

    // ---- ResizeTarget：SE 目标点，下限 MinSize、不越画布 ----

    [Fact]
    public void ResizeTarget_NormalSize_ReturnsCorner()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 10, 8, 100, 100);
        Assert.Equal(new SKPoint(12, 10), t);
    }

    [Fact]
    public void ResizeTarget_TooSmall_ClampsToMin()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 1, 1, 100, 100);
        Assert.Equal(new SKPoint(6, 6), t); // w = h = MinSize(4)
    }

    [Fact]
    public void ResizeTarget_BeyondCanvas_ClampsToImageEdge()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 500, 500, 100, 100);
        Assert.Equal(new SKPoint(100, 100), t);
    }

    // ---- GeometryRect：逻辑几何矩形（手柄空间，不含描边外扩） ----

    [Fact]
    public void GeometryRect_Rectangle_IsLogicalNotInflated()
    {
        var a = Rect(2, 3, 10, 10); // 默认描边宽 2
        Assert.Equal(2f, a.GeometryRect.Left);   // 逻辑矩形
        Assert.Equal(10f, a.GeometryRect.Width);
        Assert.Equal(1f, a.BoundingBox.Left);    // dirty 口径：外扩半个 stroke
        Assert.Equal(12f, a.BoundingBox.Width);
    }

    [Fact]
    public void GeometryRect_PixelEffect_IsRegion()
    {
        var a = new MosaicAnnotation(new SKRect(4, 4, 20, 20), new SKBitmap(40, 40), AnnotationStyle.Default);
        Assert.Equal(a.Region, a.GeometryRect);
    }

    [Fact]
    public void GeometryRect_Default_EqualsBoundingBox()
    {
        var a = new LineAnnotation(new SKPoint(0, 0), new SKPoint(9, 9), AnnotationStyle.Default);
        Assert.Equal(a.BoundingBox, a.GeometryRect);
    }
}
