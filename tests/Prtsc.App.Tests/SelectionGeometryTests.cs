using Avalonia;
using Prtsc.App.Controls;
using G = Prtsc.App.Controls.SelectionGeometry;

namespace Prtsc.App.Tests;

public class SelectionGeometryTests
{
    // ---------- FromCorners ----------

    [Fact]
    public void FromCorners_Orders_MinMax()
    {
        var r = G.FromCorners(100, 200, 50, 80);
        Assert.Equal(new Rect(50, 80, 50, 120), r);
    }

    [Fact]
    public void FromCorners_Zero_Size_When_Equal()
    {
        var r = G.FromCorners(10, 20, 10, 20);
        Assert.Equal(new Rect(10, 20, 0, 0), r);
    }

    // ---------- ClampAndNormalize ----------

    [Fact]
    public void Clamp_Trims_To_Bounds()
    {
        var r = G.ClampAndNormalize(new Rect(-10, -10, 200, 200), 100, 100);
        Assert.Equal(new Rect(0, 0, 100, 100), r);
    }

    [Fact]
    public void Clamp_Handles_Negative_Width_By_Flipping()
    {
        // Right < X → 翻转成正 rect
        var raw = new Rect(80, 80, -30, -40);
        var r = G.ClampAndNormalize(raw, 100, 100);
        Assert.Equal(new Rect(50, 40, 30, 40), r);
    }

    // ---------- HitTest ----------

    [Fact]
    public void HitTest_NoSelection_ReturnsOutside()
    {
        Assert.Equal(G.Grip.Outside, G.HitTest(null, new Point(10, 10), 8, 6));
    }

    [Fact]
    public void HitTest_Corner_Beats_Edge()
    {
        var sel = new Rect(100, 100, 200, 100);
        // 左上角
        Assert.Equal(G.Grip.NW, G.HitTest(sel, new Point(100, 100), 8, 6));
        Assert.Equal(G.Grip.NE, G.HitTest(sel, new Point(300, 100), 8, 6));
        Assert.Equal(G.Grip.SE, G.HitTest(sel, new Point(300, 200), 8, 6));
        Assert.Equal(G.Grip.SW, G.HitTest(sel, new Point(100, 200), 8, 6));
    }

    [Fact]
    public void HitTest_EdgeMidpoint_MapsToEdgeGrip()
    {
        var sel = new Rect(100, 100, 200, 100);
        Assert.Equal(G.Grip.N, G.HitTest(sel, new Point(200, 100), 8, 6));
        Assert.Equal(G.Grip.S, G.HitTest(sel, new Point(200, 200), 8, 6));
        Assert.Equal(G.Grip.W, G.HitTest(sel, new Point(100, 150), 8, 6));
        Assert.Equal(G.Grip.E, G.HitTest(sel, new Point(300, 150), 8, 6));
    }

    [Fact]
    public void HitTest_EdgeLine_Between_Handles()
    {
        var sel = new Rect(100, 100, 200, 100);
        // 上边中段（远离角/中点），落在 slop 范围内 → 归为 N
        Assert.Equal(G.Grip.N, G.HitTest(sel, new Point(130, 103), 8, 6));
    }

    [Fact]
    public void HitTest_Inside_And_Outside()
    {
        var sel = new Rect(100, 100, 200, 100);
        Assert.Equal(G.Grip.Inside, G.HitTest(sel, new Point(200, 150), 8, 6));
        Assert.Equal(G.Grip.Outside, G.HitTest(sel, new Point(50, 50), 8, 6));
    }

    // ---------- ApplyDrag ----------

    [Fact]
    public void ApplyDrag_Inside_Translates()
    {
        var start = new Rect(100, 100, 50, 50);
        var r = G.ApplyDrag(start, 10, 20, G.Grip.Inside);
        Assert.Equal(new Rect(110, 120, 50, 50), r);
    }

    [Fact]
    public void ApplyDrag_SE_Expands()
    {
        var start = new Rect(100, 100, 50, 50);
        var r = G.ApplyDrag(start, 30, 40, G.Grip.SE);
        Assert.Equal(new Rect(100, 100, 80, 90), r);
    }

    [Fact]
    public void ApplyDrag_NW_Contracts_TL_But_Keeps_BR()
    {
        var start = new Rect(100, 100, 50, 50);
        // TL 向右下 10 → 右下角保持 (150, 150), 新 TL (110, 110), 尺寸 40x40
        var r = G.ApplyDrag(start, 10, 10, G.Grip.NW);
        Assert.Equal(new Rect(110, 110, 40, 40), r);
    }

    [Fact]
    public void ApplyDrag_NW_Past_Opposite_Flips_Via_FromCorners()
    {
        var start = new Rect(100, 100, 50, 50);
        // TL 向右下拉过 BR → 生成翻转矩形
        var r = G.ApplyDrag(start, 80, 80, G.Grip.NW);
        // 对角是 (150,150)，新 "TL" 移到 (180,180)，最终 FromCorners → x=150,y=150,w=30,h=30
        Assert.Equal(new Rect(150, 150, 30, 30), r);
    }

    [Fact]
    public void ApplyDrag_N_Only_Moves_TopEdge()
    {
        var start = new Rect(100, 100, 50, 50);
        var r = G.ApplyDrag(start, 999, -10, G.Grip.N);  // dx 应被忽略
        Assert.Equal(new Rect(100, 90, 50, 60), r);
    }

    // ---------- ApplyAspectLock ----------

    [Fact]
    public void AspectLock_Corner_Preserves_Ratio()
    {
        var start = new Rect(0, 0, 200, 100);  // 2:1
        var candidate = new Rect(0, 0, 300, 100); // 用户拉宽了
        var r = G.ApplyAspectLock(candidate, start, G.Grip.SE);
        // 主边 W=300，H = 300/2 = 150
        Assert.Equal(new Rect(0, 0, 300, 150), r);
    }

    [Fact]
    public void AspectLock_NoOp_On_Edge_Grip()
    {
        var start = new Rect(0, 0, 200, 100);
        var candidate = new Rect(0, 0, 300, 100);
        var r = G.ApplyAspectLock(candidate, start, G.Grip.E);
        Assert.Equal(candidate, r); // 边中 handle 不锁比例
    }

    [Fact]
    public void AspectLock_NW_Keeps_Bottom_Right_Anchor()
    {
        var start = new Rect(100, 100, 200, 100); // BR = (300, 200)
        // 用户把 TL 拖到 (50, 60) → candidate 300 x 140
        var candidate = new Rect(50, 60, 250, 140);
        var r = G.ApplyAspectLock(candidate, start, G.Grip.NW);
        // 应该保持 BR = (300, 200)
        Assert.Equal(300, r.Right, 3);
        Assert.Equal(200, r.Bottom, 3);
        // 比例仍是 2:1
        Assert.Equal(2.0, r.Width / r.Height, 3);
    }

    // ---------- ApplyCenterSymmetric ----------

    [Fact]
    public void CenterSymmetric_Corner_Mirrors_Across_Center()
    {
        var start = new Rect(100, 100, 100, 100); // center (150, 150)
        // 拉 SE 到 (220, 220) → half = 70,70
        var candidate = new Rect(100, 100, 120, 120);
        var r = G.ApplyCenterSymmetric(candidate, start, G.Grip.SE);
        Assert.Equal(new Rect(80, 80, 140, 140), r);
    }

    [Fact]
    public void CenterSymmetric_N_Only_Adjusts_Height()
    {
        var start = new Rect(100, 100, 100, 100); // center (150, 150)
        // 把 N 向上拉 30 → candidate rect y=70
        var candidate = new Rect(100, 70, 100, 130);
        var r = G.ApplyCenterSymmetric(candidate, start, G.Grip.N);
        // halfH = max(|70-150|, |200-150|) = 80 → y = 70, h = 160
        Assert.Equal(new Rect(100, 70, 100, 160), r);
    }

    // ---------- ComputeToolRingPosition ----------

    private static readonly Size Ring = new(80, 40);
    private static readonly Size Overlay = new(1920, 1080);
    private const double Gap = 8;

    [Fact]
    public void ToolRing_Prefers_Below_When_Room()
    {
        var sel = new Rect(100, 100, 400, 200); // bottom = 300
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        Assert.Equal(new Point(100, 308), pos);
    }

    [Fact]
    public void ToolRing_Falls_Back_To_Right_When_No_Room_Below()
    {
        // 选框贴底：下方无空间，右侧应有
        var sel = new Rect(100, 1000, 400, 80); // bottom = 1080 == overlay.H
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        Assert.Equal(new Point(508, 1000), pos); // sel.Right + gap = 508
    }

    [Fact]
    public void ToolRing_Falls_Back_To_Above_When_No_Room_Below_Or_Right()
    {
        // 选框贴底右：下、右皆无
        var sel = new Rect(1500, 1000, 420, 80); // Bottom=1080, Right=1920
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        Assert.Equal(new Point(1500, 1000 - 40 - 8), pos); // 上方 y = 952
    }

    [Fact]
    public void ToolRing_Falls_Back_To_Left_When_Only_Left_Fits()
    {
        // 选框占据顶右下：下/右/上皆挤
        var sel = new Rect(1500, 0, 420, 1080);  // Bottom=overlay.H, Right=overlay.W, top=0
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        // 左侧 x = 1500 - 80 - 8 = 1412
        Assert.Equal(new Point(1412, 0), pos);
    }

    [Fact]
    public void ToolRing_Inside_Fallback_When_All_Sides_Blocked()
    {
        // 选框覆盖整个 overlay：无外围空间
        var sel = new Rect(0, 0, Overlay.Width, Overlay.Height);
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        // 内嵌右下角：x = min(right - ring - gap, overlay - ring); y = 类似
        var expectedX = Math.Clamp(Overlay.Width - Ring.Width - Gap, Gap, Overlay.Width - Ring.Width);
        var expectedY = Math.Clamp(Overlay.Height - Ring.Height - Gap, Gap, Overlay.Height - Ring.Height);
        Assert.Equal(new Point(expectedX, expectedY), pos);
    }

    [Fact]
    public void ToolRing_Below_Clamps_X_When_Selection_Near_Right_Edge()
    {
        // 选框宽度小、贴右边：下方位置 X 应被裁到 overlay.Width - ring.Width
        var sel = new Rect(1900, 100, 20, 100); // Right=1920, Bottom=200
        var pos = G.ComputeToolRingPosition(sel, Ring, Overlay, Gap);
        // 优先下方；X clamp 到 1920 - 80 = 1840
        Assert.Equal(new Point(1840, 208), pos);
    }
}
