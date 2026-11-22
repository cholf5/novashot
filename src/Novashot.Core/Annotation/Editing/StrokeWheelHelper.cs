namespace Novashot.Core.Annotation.Editing;

/// <summary>
/// Ctrl+滚轮改描边宽度的步进策略。放 Core 便于测试与 M8 CLI 复用。
///
/// 步进语义：Δy &gt; 0（滚轮上滑）→ +1；Δy &lt; 0（下滑）→ -1；Δy == 0 保持不变。
/// 结果始终 clamp 到 <c>[min, max]</c>（默认 1..16，与 StylePanel Slider 一致）。
/// </summary>
public static class StrokeWheelHelper
{
    public const float DefaultMin = 1f;
    public const float DefaultMax = 16f;

    public static float Step(float current, double wheelDeltaY, float min = DefaultMin, float max = DefaultMax)
    {
        float step = wheelDeltaY > 0 ? +1f : wheelDeltaY < 0 ? -1f : 0f;
        var next = current + step;
        if (next < min) next = min;
        if (next > max) next = max;
        return next;
    }
}
