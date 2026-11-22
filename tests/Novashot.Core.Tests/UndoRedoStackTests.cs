using Novashot.Core.Annotation.History;

namespace Novashot.Core.Tests;

public class UndoRedoStackTests
{
    private sealed class Op : IUndoableCommand
    {
        private readonly Action _do;
        private readonly Action _undo;
        public string Description { get; }

        public int DoCount { get; private set; }
        public int UndoCount { get; private set; }

        public Op(string desc, Action? doAction = null, Action? undoAction = null)
        {
            Description = desc;
            _do = doAction ?? (() => { });
            _undo = undoAction ?? (() => { });
        }

        public void Do()   { _do();   DoCount++; }
        public void Undo() { _undo(); UndoCount++; }
    }

    [Fact]
    public void Execute_runs_Do_and_toggles_CanUndo()
    {
        var stack = new UndoRedoStack();
        var op = new Op("a");
        stack.Execute(op);

        Assert.Equal(1, op.DoCount);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_then_Redo_restores_state()
    {
        var stack = new UndoRedoStack();
        var op = new Op("a");
        stack.Execute(op);

        Assert.True(stack.Undo());
        Assert.Equal(1, op.UndoCount);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);

        Assert.True(stack.Redo());
        Assert.Equal(2, op.DoCount);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Execute_after_Undo_clears_Redo()
    {
        var stack = new UndoRedoStack();
        stack.Execute(new Op("a"));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(new Op("b"));
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Capacity_evicts_oldest_from_undo_stack()
    {
        var stack = new UndoRedoStack(capacity: 2);
        stack.Execute(new Op("a"));
        stack.Execute(new Op("b"));
        stack.Execute(new Op("c"));

        // 只应留 b, c
        Assert.Equal(2, stack.UndoCount);
        Assert.True(stack.Undo()); // c
        Assert.True(stack.Undo()); // b
        Assert.False(stack.Undo()); // a 已被逐出
    }

    [Fact]
    public void Clear_resets_both_stacks_and_fires_once()
    {
        var stack = new UndoRedoStack();
        stack.Execute(new Op("a"));
        stack.Undo();

        var fired = 0;
        stack.StateChanged += (_, _) => fired++;

        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Undo_on_empty_returns_false()
    {
        var stack = new UndoRedoStack();
        Assert.False(stack.Undo());
        Assert.False(stack.Redo());
    }

    [Fact]
    public void StateChanged_fires_on_transitions_not_every_execute()
    {
        var stack = new UndoRedoStack();
        var fires = 0;
        stack.StateChanged += (_, _) => fires++;

        stack.Execute(new Op("a")); // undo: 0→1（触发一次）
        stack.Execute(new Op("b")); // undo: 1→2（不触发）
        stack.Undo();               // undo: 2→1（不触发），redo: 0→1（触发）
        stack.Undo();               // undo: 1→0（触发），redo: 1→2（不触发）

        Assert.Equal(3, fires);
    }
}
