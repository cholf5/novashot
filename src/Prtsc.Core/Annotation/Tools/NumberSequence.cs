using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// Number 序号分配策略：新号 = 现存 <see cref="NumberAnnotation"/> 中最大 <c>Number</c> + 1。
/// 空文档从 1 起。这样删除中间某号后再落，新号是 max+1（Flameshot 行为）；
/// 若最新号被 undo 或删除，下一个新号自动回落到 max+1，序号"天然回收"。
/// </summary>
public static class NumberSequence
{
    public static int NextFor(IReadOnlyList<AnnotationBase> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        int max = 0;
        for (int i = 0; i < annotations.Count; i++)
        {
            if (annotations[i] is NumberAnnotation n && n.Number > max)
                max = n.Number;
        }
        return max + 1;
    }
}
