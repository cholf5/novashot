using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 序号分配（winshot 语义）：新号 = 现存编号中<b>最小未使用的正整数</b>——
/// 删除/Undo 中间的号后，下一个新号自动回收空位；全部连续时等于 max+1。
/// </summary>
public static class NumberSequenceMinUnused
{
    public static int NextFor(IReadOnlyList<AnnotationBase> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var used = new HashSet<int>();
        for (int i = 0; i < annotations.Count; i++)
        {
            if (annotations[i] is NumberAnnotation n && n.Number > 0)
                used.Add(n.Number);
        }

        int candidate = 1;
        while (used.Contains(candidate))
            candidate++;
        return candidate;
    }
}
