namespace Novashot.Core.Windows;

/// <summary>
/// 截图 overlay 的「点击 vs 拖拽」手势分类状态机（纯逻辑，无 UI 依赖）。
///
/// 统一交互（docs/plans/2026-11-23-unified-capture-overlay-design.md）：未拖动时悬停吸附窗口，
/// 原地释放（位移 &lt; 阈值）= 确认吸附窗口；位移越过阈值 = 转入区域拖拽（候选作废）。
/// 位移度量取 max(|dx|,|dy|)——任一轴的实质位移都算拖拽，斜向细拖不漏判。
/// 阈值由调用方按 SelectionSession 的 MinSize 清框门限对齐换算（App 侧 = MinSize × scale），
/// 保证「拖出有效选框 → 区域」与「没成框 → 窗口/无动作」两类结局互斥不重叠。
///
/// 坐标口径由调用方定（App 侧喂物理像素），阈值与点位必须同口径。
/// Press 幂等：重复 Press 覆盖先前状态（指针捕获丢失等异常流不悬挂）。
/// </summary>
public sealed class SnapGesture
{
    public enum Phase { Idle, Pressed, Dragging }

    /// <summary>默认位移阈值数值（DIP 口径；调用方换算到自身坐标口径）。</summary>
    public const double DefaultThresholdDip = 4.0;

    private Phase _phase = Phase.Idle;
    private double _anchorX;
    private double _anchorY;

    public SnapGesture(double threshold)
    {
        if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        Threshold = threshold;
    }

    public double Threshold { get; }

    public Phase CurrentPhase => _phase;

    /// <summary>指针按下：记录锚点进入 Pressed（覆盖先前任何状态）。</summary>
    public void Press(double x, double y)
    {
        _phase = Phase.Pressed;
        _anchorX = x;
        _anchorY = y;
    }

    /// <summary>
    /// 按下期间的移动：位移越过阈值时作废候选转 Dragging 并返回 true（调用方清吸附预览）；
    /// 未越过返回 false。Idle（悬停）或已在 Dragging 时的移动恒 false。
    /// </summary>
    public bool MarkDragIfBeyond(double x, double y)
    {
        if (_phase != Phase.Pressed) return false;
        var dx = Math.Abs(x - _anchorX);
        var dy = Math.Abs(y - _anchorY);
        if (Math.Max(dx, dy) < Threshold) return false;
        _phase = Phase.Dragging;
        return true;
    }

    /// <summary>
    /// 释放：Press→Release 期间位移始终未越阈（真点击）返回 true，否则 false。释放后回 Idle。
    /// </summary>
    public bool Release()
    {
        var wasClick = _phase == Phase.Pressed;
        _phase = Phase.Idle;
        return wasClick;
    }

    /// <summary>强制复位（指针捕获丢失等异常流）。</summary>
    public void Reset() => _phase = Phase.Idle;
}
