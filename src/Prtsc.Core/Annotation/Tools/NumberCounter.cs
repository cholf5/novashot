namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 每编辑器一份的单调自增计数器，供 <see cref="NumberTool"/> 分配序号。
///
/// 设计约定：不随 undo 回滚（简化实现），因此 undo 后再新增会出现序号跳跃；
/// M4d/M5 可加"重新排序"命令做序号归一。
/// </summary>
public sealed class NumberCounter
{
    private int _next;

    public NumberCounter(int start = 1)
    {
        if (start < 1) throw new ArgumentOutOfRangeException(nameof(start));
        _next = start;
    }

    public int Peek() => _next;

    public int Next() => _next++;

    public void Reset(int start = 1)
    {
        if (start < 1) throw new ArgumentOutOfRangeException(nameof(start));
        _next = start;
    }
}
