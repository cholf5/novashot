using Novashot.Core.Windows;
using Xunit;

namespace Novashot.Core.Tests.Windows;

/// <summary>
/// 「点击 vs 拖拽」手势分类状态机：原地释放 = 点击（确认吸附窗口）、越阈 = 拖拽（候选作废）。
/// 交互契约见 docs/plans/2026-11-23-unified-capture-overlay-design.md。
/// </summary>
public class SnapGestureTests
{
    private static SnapGesture NewGesture(double threshold = SnapGesture.DefaultThresholdDip) => new(threshold);

    [Fact]
    public void Press_ReleaseWithoutMove_IsClick()
    {
        var g = NewGesture();
        g.Press(100, 100);

        Assert.True(g.Release());
        Assert.Equal(SnapGesture.Phase.Idle, g.CurrentPhase);
    }

    [Fact]
    public void Press_MoveWithinThreshold_IsStillClick()
    {
        var g = NewGesture(threshold: 4);
        g.Press(100, 100);

        Assert.False(g.MarkDragIfBeyond(102.9, 101.9)); // max(|dx|,|dy|) < 4

        Assert.True(g.Release());
    }

    [Theory]
    [InlineData(104.1, 100)]  // x 轴越阈
    [InlineData(100, 104.1)]  // y 轴越阈
    [InlineData(103, 105)]    // 斜向：任一轴越阈即拖拽（max 度量）
    public void Press_MoveBeyondThreshold_VoidsCandidate(double x, double y)
    {
        var g = NewGesture(threshold: 4);
        g.Press(100, 100);

        Assert.True(g.MarkDragIfBeyond(x, y));
        Assert.Equal(SnapGesture.Phase.Dragging, g.CurrentPhase);

        Assert.False(g.Release()); // 拖拽不是点击
        Assert.Equal(SnapGesture.Phase.Idle, g.CurrentPhase);
    }

    [Fact]
    public void MarkDrag_BoundaryExactlyAtThreshold_VoidsCandidate()
    {
        var g = NewGesture(threshold: 4);
        g.Press(100, 100);

        // 阈值语义为「越过即拖拽」：位移 == 阈值也判拖拽（与 Session「== MinSize 的选框保留」对齐）
        Assert.True(g.MarkDragIfBeyond(104, 100));
    }

    [Fact]
    public void MoveWhileIdle_IsNeverDrag()
    {
        var g = NewGesture();

        Assert.False(g.MarkDragIfBeyond(9999, 9999)); // 悬停移动
        Assert.False(g.Release());                    // Idle 释放不是点击
    }

    [Fact]
    public void MoveWhileDragging_StaysFalse_NoDoubleVoid()
    {
        var g = NewGesture(threshold: 4);
        g.Press(0, 0);

        Assert.True(g.MarkDragIfBeyond(100, 0));  // 首次越阈 true
        Assert.False(g.MarkDragIfBeyond(200, 0)); // 已是 Dragging，不再重复报
    }

    [Fact]
    public void Press_OverwritesAnyPriorState_NoStuckPhase()
    {
        var g = NewGesture(threshold: 4);
        g.Press(0, 0);
        g.MarkDragIfBeyond(100, 0); // Dragging
        g.Press(50, 50);            // 捕获丢失后重新按下：覆盖，不悬挂

        Assert.Equal(SnapGesture.Phase.Pressed, g.CurrentPhase);
        Assert.False(g.MarkDragIfBeyond(52, 52));
        Assert.True(g.Release());
    }

    [Fact]
    public void Reset_ForcesIdle()
    {
        var g = NewGesture();
        g.Press(0, 0);
        g.Reset();

        Assert.Equal(SnapGesture.Phase.Idle, g.CurrentPhase);
        Assert.False(g.Release());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveThreshold_Throws(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapGesture(threshold));
    }
}
