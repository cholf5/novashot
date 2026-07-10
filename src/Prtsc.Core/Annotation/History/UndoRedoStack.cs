namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 双栈撤销 / 重做管理器，默认容量 50（超出从最老一端逐出）。
///
/// - <see cref="Execute"/>：调用 command.Do()，压入 undo 栈，清空 redo 栈。
/// - <see cref="Undo"/>：从 undo 栈弹出并 Undo()，压入 redo 栈。
/// - <see cref="Redo"/>：从 redo 栈弹出并 Do()，压入 undo 栈。
/// - <see cref="Clear"/>：清空两个栈。
///
/// 用 <see cref="LinkedList{T}"/> 而非 <see cref="Stack{T}"/> 是为了能从头逐出以维持容量上限。
/// </summary>
public sealed class UndoRedoStack
{
    public const int DefaultCapacity = 50;

    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly LinkedList<IUndoableCommand> _redo = new();

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>CanUndo / CanRedo 任一发生变化时触发（一次调用可能同时改变两者）。</summary>
    public event EventHandler? StateChanged;

    public UndoRedoStack(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    /// <summary>执行并入栈。会清空 redo 栈。</summary>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var hadRedo = _redo.Count > 0;
        var hadUndo = _undo.Count > 0;

        command.Do();
        _undo.AddLast(command);
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();
        _redo.Clear();

        if (!hadUndo || hadRedo) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var hadRedo = _redo.Count > 0;

        var cmd = _undo.Last!.Value;
        _undo.RemoveLast();
        cmd.Undo();
        _redo.AddLast(cmd);
        while (_redo.Count > Capacity)
            _redo.RemoveFirst();

        // Undo 栈可能变空；Redo 栈从空变非空
        if (_undo.Count == 0 || !hadRedo) StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var hadUndo = _undo.Count > 0;

        var cmd = _redo.Last!.Value;
        _redo.RemoveLast();
        cmd.Do();
        _undo.AddLast(cmd);
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();

        if (_redo.Count == 0 || !hadUndo) StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        var had = _undo.Count > 0 || _redo.Count > 0;
        _undo.Clear();
        _redo.Clear();
        if (had) StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
