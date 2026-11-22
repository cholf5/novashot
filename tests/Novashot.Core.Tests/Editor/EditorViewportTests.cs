using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests.Editor;

/// <summary>
/// 编辑器视口数学：fit 基础矩阵、滚轮锚点缩放（锚点不动点）、zoom/pan clamp 边界、
/// 「100% 物理像素」绝对缩放与复位。基准几何：视口 1000x800，内容 4000x3000 → fit scale = 0.25。
/// </summary>
public class EditorViewportTests
{
    private const double Vw = 1000;
    private const double Vh = 800;
    private static readonly SKSize Content = new(4000, 3000);

    private static EditorViewport FitViewport()
    {
        var v = new EditorViewport();
        v.Clamp(Vw, Vh, Content); // fit 态 clamp 应为恒等（pan 归零）
        return v;
    }

    private static SKPoint InverseMap(SKMatrix m, SKPoint p)
    {
        Assert.True(m.TryInvert(out var inv));
        return inv.MapPoint(p);
    }

    [Fact]
    public void FitMatrix_ScalesAndCenters()
    {
        var m = EditorViewport.ComputeFitMatrix(Vw, Vh, Content);
        Assert.Equal(0.25, m.ScaleX, 1e-6);
        Assert.Equal(0.25, m.ScaleY, 1e-6);
        // 4000*0.25 = 1000 恰好占满宽 → tx=0；3000*0.25 = 750 < 800 → ty=25
        Assert.Equal(0, m.TransX, 1e-6);
        Assert.Equal(25, m.TransY, 1e-6);
    }

    [Fact]
    public void ZoomAt_KeepsAnchorContentPointFixed()
    {
        var v = FitViewport();
        var anchor = new SKPoint(500, 400);
        var img = InverseMap(v.ComputeMatrix(Vw, Vh, Content), anchor);

        v.ZoomAt(anchor, 2, Vw, Vh, Content);

        var mapped = v.ComputeMatrix(Vw, Vh, Content).MapPoint(img);
        Assert.Equal(anchor.X, mapped.X, 1e-3);
        Assert.Equal(anchor.Y, mapped.Y, 1e-3);
        Assert.Equal(0.5, v.ComputeMatrix(Vw, Vh, Content).ScaleX, 1e-6);
    }

    [Fact]
    public void ZoomAt_ClampsToLowerBound_AndKeepsPanCenteredAtFit()
    {
        var v = FitViewport();
        v.ZoomAt(new SKPoint(500, 400), 0.01, Vw, Vh, Content);

        Assert.Equal(EditorViewport.MinZoom, v.Zoom);
        Assert.Equal(EditorViewport.ComputeFitMatrix(Vw, Vh, Content), v.ComputeMatrix(Vw, Vh, Content));
    }

    [Fact]
    public void ZoomAt_ClampsToUpperBound()
    {
        var v = FitViewport();
        v.ZoomAt(new SKPoint(500, 400), 100, Vw, Vh, Content);
        Assert.Equal(EditorViewport.MaxZoom, v.Zoom);

        v.ZoomAt(new SKPoint(500, 400), 2, Vw, Vh, Content); // 封顶后连乘不再放大
        Assert.Equal(EditorViewport.MaxZoom, v.Zoom);
    }

    [Fact]
    public void ZoomToAbsolute_SetsScale_AndClampsBelowFit()
    {
        var v = FitViewport();
        v.ZoomToAbsolute(1.0, Vw, Vh, Content); // fit 0.25 → zoom 4
        Assert.Equal(1.0, v.ComputeMatrix(Vw, Vh, Content).ScaleX, 1e-6);

        var below = FitViewport();
        below.ZoomToAbsolute(0.1, Vw, Vh, Content); // 目标低于 fit → 回 fit
        Assert.Equal(EditorViewport.MinZoom, below.Zoom);
        Assert.Equal(0, below.Pan.X, 1e-6);
    }

    [Fact]
    public void PanBy_ClampsToKeepContentCoveringViewport()
    {
        var v = FitViewport();
        v.ZoomToAbsolute(1.0, Vw, Vh, Content); // 内容屏上 4000x3000 ≥ 视口
        // 中心锚 pan 初值 (-1500, -1200)，均在 clamp 区间内
        Assert.Equal(-1500, v.Pan.X, 1e-4);
        Assert.Equal(-1200, v.Pan.Y, 1e-4);

        v.PanBy(new SKPoint(5000, 0), Vw, Vh, Content);
        Assert.Equal(0, v.Pan.X, 1e-6); // x ∈ [-3000, 0]

        v.PanBy(new SKPoint(-5000, 0), Vw, Vh, Content);
        Assert.Equal(-3000, v.Pan.X, 1e-6);

        v.PanBy(new SKPoint(0, -9000), Vw, Vh, Content);
        Assert.Equal(-2300, v.Pan.Y, 1e-6); // y ∈ [-2300, -100]（fit 竖向有 25 边距 ×4）
    }

    [Fact]
    public void PanBy_AtFit_PinPanToCenter()
    {
        var v = FitViewport();
        v.PanBy(new SKPoint(300, 300), Vw, Vh, Content);

        Assert.Equal(0, v.Pan.X, 1e-6); // fit 态内容 ≤ 视口，pan 锁居中
        Assert.Equal(0, v.Pan.Y, 1e-6);
    }

    [Fact]
    public void Clamp_CentersAxisThatContentCannotCover()
    {
        // 内容 500x500，fit scale = 1.6（dx=100, dy=0）；zoom 1.2 → 屏上 960x960：
        // x 盖不住 1000 → 锁居中（pan=-100），y 恰好盖住 800 → 保持锚点位置
        var v = new EditorViewport();
        var content = new SKSize(500, 500);
        v.ZoomAt(new SKPoint(500, 400), 1.2, Vw, Vh, content);

        Assert.Equal(-100, v.Pan.X, 1e-4);
        var r = v.ComputeMatrix(Vw, Vh, content).MapRect(new SKRect(0, 0, 500, 500));
        Assert.Equal(20, r.Left, 1e-4);
        Assert.Equal(980, r.Right, 1e-4);
    }

    [Fact]
    public void Reset_RestoresFitMatrix()
    {
        var v = FitViewport();
        v.ZoomAt(new SKPoint(300, 300), 3, Vw, Vh, Content);
        v.PanBy(new SKPoint(-200, 100), Vw, Vh, Content);

        v.Reset();

        Assert.Equal(EditorViewport.MinZoom, v.Zoom);
        Assert.Equal(EditorViewport.ComputeFitMatrix(Vw, Vh, Content), v.ComputeMatrix(Vw, Vh, Content));
    }

    [Fact]
    public void DegenerateContent_ReturnsIdentity()
        => Assert.Equal(SKMatrix.Identity, EditorViewport.ComputeFitMatrix(Vw, Vh, new SKSize(0, 300)));
}
