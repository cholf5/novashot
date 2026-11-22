using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// E-1 M9：会话级色值历史（非 config 持久化，跨会话清空可接受——卡原文「会话内最近 N 个」）。
/// 记录来源：overlay 取色点击；StylePanel 历史行消费（点历史色 = 设为描边色）。
/// 最近 10 色、按 RGB 去重、最新在前；每次变更发 <see cref="Changed"/> 让面板刷新。
/// UI 线程专用对象，锁只防测试/未来跨线程订阅时的撕裂。
/// </summary>
public sealed class ColorHistory
{
    /// <summary>卡原文：会话内最近 N 个，N=10。</summary>
    public const int DefaultCapacity = 10;

    private readonly object _gate = new();
    private readonly List<SKColor> _colors = new();
    private readonly int _capacity;

    public ColorHistory(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    /// <summary>容量（默认 10）。</summary>
    public int Capacity => _capacity;

    /// <summary>当前历史（最新在前）。返回快照，后续 Add 不影响已返回的列表。</summary>
    public IReadOnlyList<SKColor> Colors
    {
        get
        {
            lock (_gate) return _colors.ToArray();
        }
    }

    /// <summary>历史色变更（新增/去重上浮/清空）。面板订阅它刷新历史行。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 记录一次取色。重复色只保留一份并上浮到最新位；超出容量挤掉最旧。
    /// </summary>
    public void Add(SKColor color)
    {
        bool changed;
        lock (_gate)
        {
            _colors.RemoveAll(c => c.Red == color.Red && c.Green == color.Green && c.Blue == color.Blue);
            _colors.Insert(0, color);
            if (_colors.Count > _capacity) _colors.RemoveRange(_capacity, _colors.Count - _capacity);
            changed = true;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清空（会话结束或面板显式清空时用）。</summary>
    public void Clear()
    {
        bool hadAny;
        lock (_gate)
        {
            hadAny = _colors.Count > 0;
            _colors.Clear();
        }
        if (hadAny) Changed?.Invoke(this, EventArgs.Empty);
    }
}
