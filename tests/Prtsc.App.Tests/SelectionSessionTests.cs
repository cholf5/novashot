using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Prtsc.App.Controls;
using Prtsc.Core.Common;
using Xunit;

using PixelRect = Prtsc.Core.Common.PixelRect;

namespace Prtsc.App.Tests;

/// <summary>
/// B-3 M2-3b 跨屏选框：SelectionSession 协调器纯逻辑回归（无窗口）。
/// 权威选框存放在「活动帧」（按下时所在屏的 DIP 坐标系）；输入按上报屏换算物理像素，
/// 再映射进活动帧；渲染按目标屏各自的 ScaleFactor 映射出界内/界外矩形。
/// 单屏（N=1）行为必须与旧 SelectionAdorner 本地数学完全一致（等价守卫）。
/// </summary>
public class SelectionSessionTests
{
    private static DisplayInfo D(int index, int x, int y, int w, int h, double scale, bool primary = false) =>
        new(index, $"D{index}", new PixelRect(x, y, w, h), new PixelRect(x, y, w, h), scale, primary);

    /// <summary>双屏横排混合 DPI：A=(0,0,1000,800)@1.0 主屏；B=(1000,0,1000,800)@2.0。</summary>
    private static SelectionSession TwoScreens() =>
        new(new[] { D(0, 0, 0, 1000, 800, 1.0, primary: true), D(1, 1000, 0, 1000, 800, 2.0) });

    private static SelectionSession OneScreen() =>
        new(new[] { D(0, 0, 0, 1000, 800, 1.0, primary: true) });

    // ------------------------------------------------------------------------
    //  单屏等价守卫（N=1 ≡ 旧 adorner 本地数学）
    // ------------------------------------------------------------------------
    [Fact]
    public void SingleScreen_DragCreate_MatchesLegacyMath()
    {
        var s = OneScreen();
        s.Press(0, new Point(100, 100), KeyModifiers.None);
        s.Drag(0, new Point(300, 250), KeyModifiers.None);
        s.Release(0);

        Assert.Equal(new PixelRect(100, 100, 200, 150), s.ConfirmRegion());
    }

    [Fact]
    public void SingleScreen_TinyDragRelease_ClearsSelection()
    {
        var s = OneScreen();
        s.Press(0, new Point(100, 100), KeyModifiers.None);
        s.Drag(0, new Point(102, 102), KeyModifiers.None);
        s.Release(0);

        Assert.Null(s.ConfirmRegion());
        Assert.Null(s.SelectionInFrame(0));
    }

    [Fact]
    public void SingleScreen_InsideDrag_MovesSelection()
    {
        var s = OneScreen();
        Create(s, 0, 200, 200, 400, 300);

        s.Press(0, new Point(250, 300), KeyModifiers.None);   // Inside → Moving
        s.Drag(0, new Point(270, 320), KeyModifiers.None);
        s.Release(0);

        Assert.Equal(new PixelRect(220, 220, 400, 300), s.ConfirmRegion());
    }

    // ------------------------------------------------------------------------
    //  跨屏创建（拖过屏界）
    // ------------------------------------------------------------------------
    [Fact]
    public void CrossScreenDrag_SelectionSpansBothScreens_AndRendersPerDisplay()
    {
        var s = TwoScreens();
        // 在屏 A 按下 (900,100)，拖到界外 (1500,300)：捕获窗口上报的 A 本地坐标越界仍有效
        s.Press(0, new Point(900, 100), KeyModifiers.None);
        s.Drag(0, new Point(1500, 300), KeyModifiers.None);

        Assert.Equal(new Rect(900, 100, 600, 200), s.SelectionInFrame(0));
        // 屏 B @2.0：物理 (900,100,600,200) → B 本地 DIP
        Assert.Equal(new Rect(-50, 50, 300, 100), s.SelectionInFrame(1));

        s.Release(0);
        Assert.Equal(new PixelRect(900, 100, 600, 200), s.ConfirmRegion());
    }

    [Fact]
    public void DragClampedToVirtualDesktopUnion()
    {
        var s = TwoScreens();
        s.Press(0, new Point(900, 700), KeyModifiers.None);
        s.Drag(0, new Point(2500, 1200), KeyModifiers.None); // 远超 union (0,0,2000,800)

        s.Release(0);
        Assert.Equal(new PixelRect(900, 700, 1100, 100), s.ConfirmRegion());
    }

    // ------------------------------------------------------------------------
    //  跨屏接管（在另一屏上按下既有选框）
    // ------------------------------------------------------------------------
    [Fact]
    public void PressOnSecondScreen_TakesOverMoving_InItsOwnFrame()
    {
        var s = TwoScreens();
        Create(s, 0, 900, 100, 600, 200); // 选框跨 A|B（物理）

        // 在屏 B 的切片内按下：B 本地 (125,100) = 物理 (125*2+1000, 200) ∈ 选框
        Assert.True(GripInside(s, 1, new Point(125, 100)));
        s.Press(1, new Point(125, 100), KeyModifiers.None);
        s.Drag(1, new Point(225, 150), KeyModifiers.None);   // +100,+50 B-DIP = +200,+100 物理
        s.Release(1);

        Assert.Equal(new PixelRect(1100, 200, 600, 200), s.ConfirmRegion());
        Assert.Equal(new Rect(1100, 200, 600, 200), s.SelectionInFrame(0));
    }

    [Fact]
    public void PressOnSecondScreen_CornerGrip_ResizesWithLocalScale()
    {
        var s = TwoScreens();
        Create(s, 0, 900, 100, 600, 200); // 物理 SE 角 = (1500,300) = B 本地 (250,150)

        s.Press(1, new Point(250, 150), KeyModifiers.None);  // SE handle 命中
        Assert.Equal(SelectionGeometry.Grip.SE, s.ActiveGrip);
        s.Drag(1, new Point(350, 200), KeyModifiers.None);   // +100,+50 B-DIP = +200,+100 物理
        s.Release(1);

        Assert.Equal(new PixelRect(900, 100, 800, 300), s.ConfirmRegion());
    }

    // ------------------------------------------------------------------------
    //  混合 DPI 往返与舍入
    // ------------------------------------------------------------------------
    [Fact]
    public void MixedDpi_ConfirmRegion_QuantizesEndpointsToPhysicalGrid()
    {
        var s = TwoScreens();
        // 在屏 B（@2.0）上创建，钉住端点量化语义类（B-3 review Minor-2）：
        // 拖到 (110.6,70.4) → 物理端点 (1221,141) → 回 DIP (110.5,70.5)；w = Round(100.5*2)=201。
        // 机制与旧「原始差值末尾舍入」可差 1px（@2.0：start 10.3 / end 60.2 → 旧 100px、新 99px），
        // 属有意行为：渲染同口径量化投影，确认 region = 所见即所得
        s.Press(1, new Point(10, 20), KeyModifiers.None);
        s.Drag(1, new Point(110.6, 70.4), KeyModifiers.None);
        s.Release(1);

        // w = Round(100.5*2)=201, h = Round(50.5*2)=101；物理起点 = Round 网格 (1020,40)
        Assert.Equal(new PixelRect(1020, 40, 201, 101), s.ConfirmRegion());
    }

    // ------------------------------------------------------------------------
    //  工具环归属（选框中心所在屏）
    // ------------------------------------------------------------------------
    [Fact]
    public void RingDisplayIndex_FollowsSelectionCenter()
    {
        var s = TwoScreens();
        Create(s, 0, 100, 100, 200, 150);
        Assert.Equal(0, s.RingDisplayIndex);

        Create(s, 0, 900, 100, 600, 200); // 跨屏，中心 (1200,200) 在 B
        Assert.Equal(1, s.RingDisplayIndex);
    }

    // ------------------------------------------------------------------------
    //  键盘微调（对齐旧 adorner 语义 + union 钳制）
    // ------------------------------------------------------------------------
    [Fact]
    public void Nudge_StepsAndClampToUnion()
    {
        var s = TwoScreens();
        Create(s, 0, 100, 100, 200, 150);

        s.Nudge(Key.Right, ctrl: false, shift: true);
        Assert.Equal(new PixelRect(110, 100, 200, 150), s.ConfirmRegion());

        s.Nudge(Key.Right, ctrl: true, shift: false);        // Ctrl+→ 增宽 1
        Assert.Equal(new PixelRect(110, 100, 201, 150), s.ConfirmRegion());

        // 连续左移钳在 union 左缘（x=0）
        for (var i = 0; i < 50; i++) s.Nudge(Key.Left, ctrl: false, shift: true);
        Assert.Equal(new PixelRect(0, 100, 201, 150), s.ConfirmRegion());
    }

    [Fact]
    public void Nudge_CanMoveSelectionOntoOtherScreen()
    {
        var s = TwoScreens();
        Create(s, 0, 900, 100, 200, 150);

        // 右移 550 物理像素（Shift 步长 10 × 55 次）→ 中心落进屏 B
        for (var i = 0; i < 55; i++) s.Nudge(Key.Right, ctrl: false, shift: true);
        Assert.Equal(new PixelRect(1450, 100, 200, 150), s.ConfirmRegion());
        Assert.Equal(1, s.RingDisplayIndex);
    }

    // ------------------------------------------------------------------------
    //  防御
    // ------------------------------------------------------------------------
    [Fact]
    public void PressDuringActiveDrag_IsIgnored()
    {
        var s = OneScreen();
        s.Press(0, new Point(100, 100), KeyModifiers.None);
        s.Drag(0, new Point(300, 250), KeyModifiers.None);
        s.Press(0, new Point(10, 10), KeyModifiers.None);    // 拖拽中再按：忽略
        s.Release(0);

        Assert.Equal(new PixelRect(100, 100, 200, 150), s.ConfirmRegion());
    }

    [Fact]
    public void ConfirmRegion_WithoutSelection_ReturnsNull()
    {
        var s = OneScreen();
        Assert.Null(s.ConfirmRegion());
        s.Nudge(Key.Left, ctrl: false, shift: false);        // 无选框时微调是无操作
        Assert.Null(s.ConfirmRegion());
    }

    [Fact]
    public void Clear_RemovesSelectionEverywhere()
    {
        var s = TwoScreens();
        Create(s, 0, 900, 100, 600, 200);
        s.Clear();

        Assert.Null(s.SelectionInFrame(0));
        Assert.Null(s.SelectionInFrame(1));
        Assert.Null(s.ConfirmRegion());
    }

    [Fact]
    public void ChangedEvent_FiresOnEveryTransition()
    {
        var s = OneScreen();
        var count = 0;
        s.Changed += (_, _) => count++;

        Create(s, 0, 100, 100, 200, 150);   // Press + Drag + Release = 3 次
        Assert.True(count >= 3);
    }

    // ------------------------------------------------------------------------
    //  helpers
    // ------------------------------------------------------------------------
    /// <summary>按下→拖拽→释放三连，按目标矩形语义（左上角 + 宽高）拖出选框。</summary>
    private static void Create(SelectionSession s, int display, double x, double y, double w, double h)
    {
        s.Press(display, new Point(x, y), KeyModifiers.None);
        s.Drag(display, new Point(x + w, y + h), KeyModifiers.None);
        s.Release(display);
    }

    private static bool GripInside(SelectionSession s, int display, Point p)
    {
        // SelectionGeometry.HitTest 的会话内等价：仅在测试里做可读性断言用
        var rect = s.SelectionInFrame(display);
        return rect is { } r && r.Contains(p);
    }
}
