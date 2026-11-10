using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// B-4：crop 框 8 手柄 reshape 纯逻辑。
/// HitTest = BoxHandles 拓扑上的距离命中（slop 为 image 空间半径）；
/// DragTo = 手柄钳制（图像内 + 不越过对边保持 MinPx）→ ApplyHandle →
/// 比例吸附（锚定对边/对角）→ 钳回图像。Esc 恢复由调用方 ApplyCrop(原框) 完成。
/// </summary>
public class CropReshapeTests
{
    private static readonly SKRect Image = new(0, 0, 200, 100);

    // ---------- HitTest ----------

    [Fact]
    public void HitTest_CornersAndEdges_ReturnKinds()
    {
        var rect = new SKRect(10, 10, 110, 60);
        Assert.Equal(HandleKind.TopLeft, CropReshape.HitTest(rect, new SKPoint(10, 10), 8f));
        Assert.Equal(HandleKind.TopRight, CropReshape.HitTest(rect, new SKPoint(110, 10), 8f));
        Assert.Equal(HandleKind.BottomRight, CropReshape.HitTest(rect, new SKPoint(110, 60), 8f));
        Assert.Equal(HandleKind.BottomLeft, CropReshape.HitTest(rect, new SKPoint(10, 60), 8f));
        Assert.Equal(HandleKind.Top, CropReshape.HitTest(rect, new SKPoint(60, 10), 8f));
        Assert.Equal(HandleKind.Right, CropReshape.HitTest(rect, new SKPoint(110, 35), 8f));
        Assert.Equal(HandleKind.Bottom, CropReshape.HitTest(rect, new SKPoint(60, 60), 8f));
        Assert.Equal(HandleKind.Left, CropReshape.HitTest(rect, new SKPoint(10, 35), 8f));
    }

    [Fact]
    public void HitTest_OutsideSlop_ReturnsNull()
    {
        var rect = new SKRect(10, 10, 110, 60);
        // 中心（离所有手柄都远）
        Assert.Null(CropReshape.HitTest(rect, new SKPoint(60, 35), 8f));
        // 手柄旁但超出 slop
        Assert.Null(CropReshape.HitTest(rect, new SKPoint(10 + 9, 10), 8f));
    }

    [Fact]
    public void HitTest_WithinSlop_ToleratesOffset()
    {
        var rect = new SKRect(10, 10, 110, 60);
        Assert.Equal(HandleKind.TopLeft, CropReshape.HitTest(rect, new SKPoint(13, 11), 8f));
    }

    // ---------- DragTo（auto，无比例） ----------

    [Fact]
    public void DragTo_BottomRight_FreeMove()
    {
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.BottomRight,
            new SKPoint(150, 80), Image, 0, 0);
        Assert.Equal(new SKRect(10, 10, 150, 80), r);
    }

    [Fact]
    public void DragTo_TopLeft_AnchorsBottomRight()
    {
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.TopLeft,
            new SKPoint(0, 0), Image, 0, 0);
        Assert.Equal(new SKRect(0, 0, 110, 60), r);
    }

    [Fact]
    public void DragTo_EdgeTop_MovesOnlyTopEdge()
    {
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.Top,
            new SKPoint(60, 2), Image, 0, 0);
        Assert.Equal(new SKRect(10, 2, 110, 60), r);
    }

    [Fact]
    public void DragTo_ClampsHandleToImageBounds()
    {
        // BR 拖出图像右下角 → 钳回 (200,100)
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.BottomRight,
            new SKPoint(500, 500), Image, 0, 0);
        Assert.Equal(new SKRect(10, 10, 200, 100), r);
        // TL 拖出图像左上角 → 钳回 (0,0)，对角不动
        var r2 = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.TopLeft,
            new SKPoint(-50, -50), Image, 0, 0);
        Assert.Equal(new SKRect(0, 0, 110, 60), r2);
    }

    [Fact]
    public void DragTo_DoesNotCrossOppositeEdge_MinSize()
    {
        // BR 拖过对角 → 停在对角 + MinPx，不翻转
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.BottomRight,
            new SKPoint(11, 11), Image, 0, 0);
        Assert.Equal(new SKRect(10, 10, 10 + CropReshape.MinPx, 10 + CropReshape.MinPx), r);
        // Top 拖过 Bottom - MinPx（60-4=56）→ 停在 56
        var r2 = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.Top,
            new SKPoint(60, 58), Image, 0, 0);
        Assert.Equal(new SKRect(10, 60 - CropReshape.MinPx, 110, 60), r2);
    }

    [Fact]
    public void DragTo_SubMinPxInput_NoCrash_ClampedToImage()
    {
        // review C1 回归位：应用态框可被 SnapToRatio/细条拖拽压到 MinPx 以下（如 3:4 吸附 200×5 → 宽 3.75），
        // 此时对边钳制区间反转——不得抛异常，结果钳回图像内
        var narrow = new SKRect(10, 10, 13.75f, 60);
        var r = CropReshape.DragTo(narrow, HandleKind.Left, new SKPoint(0, 35), Image, 0, 0);
        Assert.InRange(r.Left, Image.Left, Image.Right);
        Assert.InRange(r.Right, Image.Left, Image.Right);
        // 贴右缘退化框：Right 手柄钳制区间 [r.Left+4, img.Right] 反转 → 收敛不炸
        var edge = new SKRect(197, 10, 200, 60);
        var r2 = CropReshape.DragTo(edge, HandleKind.Right, new SKPoint(200, 35), Image, 0, 0);
        Assert.InRange(r2.Left, Image.Left, Image.Right);
        Assert.InRange(r2.Right, Image.Left, Image.Right);
        Assert.Equal(200, r2.Right);
    }

    // ---------- DragTo（比例吸附，锚定对边/对角） ----------

    [Fact]
    public void DragTo_BottomRight_Ratio_AnchorsOppositeCorner()
    {
        // 100x50 拖 BR 到 (150,80)：raw 140x70 太宽 → 70x70，锚 (10,10)
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.BottomRight,
            new SKPoint(150, 80), Image, 1, 1);
        Assert.Equal(new SKRect(10, 10, 80, 80), r);
    }

    [Fact]
    public void DragTo_TopLeft_Ratio_AnchorsBottomRight()
    {
        // 100x50 拖 TL 到 (0,0)：raw (0,0,110,60) 即 110x60 太宽 → 60x60，锚 (110,60) → (50,0,110,60)
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.TopLeft,
            new SKPoint(0, 0), Image, 1, 1);
        Assert.Equal(new SKRect(50, 0, 110, 60), r);
    }

    [Fact]
    public void DragTo_EdgeRight_Ratio_AnchorsLeftEdge()
    {
        // 100x50 拖 Right 到 x=150：宽 = 高*ratio = 50*4/3，锚左边
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.Right,
            new SKPoint(150, 35), Image, 4, 3);
        Assert.Equal(10, r.Left);
        Assert.Equal(10, r.Top);
        Assert.Equal(60, r.Bottom);
        Assert.Equal(200f / 3f, r.Width, 3);
    }

    [Fact]
    public void DragTo_EdgeTop_Ratio_AnchorsBottomEdge()
    {
        // 100x50 (2:1) 拖 Top 到 y=20：高 = 宽/ratio = 50，锚下边 → 回到 y=10
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.Top,
            new SKPoint(60, 20), Image, 2, 1);
        Assert.Equal(new SKRect(10, 10, 110, 60), r);
    }

    [Fact]
    public void DragTo_EdgeTop_Ratio_Overflow_ScalesToFitKeepingRatio()
    {
        // 拖 Top 到 0：比例 1:10 需要高 1000 > 可用 60 → 收窄保比例：w = 60*0.1 = 6，水平居中
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.Top,
            new SKPoint(60, 0), Image, 1, 10);
        Assert.Equal(0, r.Top);
        Assert.Equal(60, r.Bottom);
        Assert.Equal(6f, r.Width, 3);
        Assert.Equal(57f, r.Left, 3);  // 中心 60 → (57..63)
        Assert.Equal(63f, r.Right, 3);
        Assert.InRange(r.Left, Image.Left, Image.Right);
        Assert.InRange(r.Right, Image.Left, Image.Right);
    }

    [Fact]
    public void DragTo_Ratio_ResultStaysInsideImage()
    {
        // 9:16 竖比例 + 贴边矩形：结果不得越出图像
        var rect = new SKRect(150, 0, 200, 100); // 50x100 贴右下
        var r = CropReshape.DragTo(rect, HandleKind.BottomRight,
            new SKPoint(200, 100), Image, 9, 16);
        Assert.InRange(r.Left, Image.Left, Image.Right);
        Assert.InRange(r.Top, Image.Top, Image.Bottom);
        Assert.InRange(r.Right, Image.Left, Image.Right);
        Assert.InRange(r.Bottom, Image.Top, Image.Bottom);
        Assert.Equal(9f / 16f, r.Width / r.Height, 3);
    }

    [Fact]
    public void DragTo_Ratio_NeverBelowMinSize()
    {
        // 极端拖拽下结果两维都不得小于 MinPx（1:1 + 对角钳到 14,14 → 精确 4×4）
        var r = CropReshape.DragTo(new SKRect(10, 10, 110, 60), HandleKind.BottomRight,
            new SKPoint(11, 11), Image, 1, 1);
        Assert.Equal(new SKRect(10, 10, 10 + CropReshape.MinPx, 10 + CropReshape.MinPx), r);
    }
}
