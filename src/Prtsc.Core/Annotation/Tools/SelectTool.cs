using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Annotation.History;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 选择/移动/改变尺寸工具。
///
/// 命中优先级：<b>当前 Selection 的手柄 → 任意标注 body（逆序 = 栈顶优先）→ 空点=清选择</b>。
///
/// 拖动语义：拖动过程中直接就地修改被选 annotation（<see cref="AnnotationBase.MoveBy"/> 或
/// <see cref="AnnotationBase.MoveHandle"/>），不产生 Preview 副本。抬手后：
/// 1. 把 annotation 状态回滚到 pre-drag，
/// 2. 用 <see cref="UndoRedoStack.Execute"/> 提交对应命令，让 Do() 重新应用，
/// 达到"栈上一条记录 + annotation 在最终位置"的一致状态。
/// </summary>
public sealed class SelectTool : IAnnotationTool
{
    /// <summary>Preview 恒为 null——SelectTool 就地改被选对象，不画预览副本。</summary>
    public AnnotationBase? Preview => null;

    private enum Mode { Idle, Moving, Resizing }

    private readonly Func<AnnotationBase?> _getSelection;
    private readonly Action<AnnotationBase?> _setSelection;
    private readonly UndoRedoStack _history;
    private readonly IReadOnlyList<AnnotationBase> _annotations;
    private readonly float _bodyHitSlop;
    private readonly float _handleHitRadius;

    private Mode _mode = Mode.Idle;
    private AnnotationBase? _target;
    private HandleKind _resizeKind;
    private SKPoint _resizeBefore;      // handle 在拖动开始前的位置（image-pixel）
    private SKPoint _moveStart;         // 移动模式起点（image-pixel）
    private SKPoint _lastMovePoint;     // 上一次移动位置（用于增量 delta）

    public SelectTool(
        Func<AnnotationBase?> getSelection,
        Action<AnnotationBase?> setSelection,
        UndoRedoStack history,
        IReadOnlyList<AnnotationBase> annotations,
        float bodyHitSlop = 4f,
        float handleHitRadius = 8f)
    {
        _getSelection = getSelection ?? throw new ArgumentNullException(nameof(getSelection));
        _setSelection = setSelection ?? throw new ArgumentNullException(nameof(setSelection));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
        _bodyHitSlop = bodyHitSlop;
        _handleHitRadius = handleHitRadius;
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        // 1. 优先测试当前 selection 的 handles
        var current = _getSelection();
        if (current is not null)
        {
            var handle = HitTestHandle(current, imagePoint);
            if (handle is { } h)
            {
                _mode = Mode.Resizing;
                _target = current;
                _resizeKind = h.Kind;
                _resizeBefore = h.ImagePos;
                return;
            }
        }

        // 2. 逆序遍历 annotations（栈顶优先）
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            var a = _annotations[i];
            if (!a.HitTestBody(imagePoint, _bodyHitSlop)) continue;

            _setSelection(a);
            _mode = Mode.Moving;
            _target = a;
            _moveStart = imagePoint;
            _lastMovePoint = imagePoint;
            return;
        }

        // 3. 空点 → 清选择
        _setSelection(null);
        _mode = Mode.Idle;
        _target = null;
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (_target is null) return;
        switch (_mode)
        {
            case Mode.Moving:
                var delta = new SKPoint(imagePoint.X - _lastMovePoint.X, imagePoint.Y - _lastMovePoint.Y);
                if (delta.X != 0 || delta.Y != 0) _target.MoveBy(delta);
                _lastMovePoint = imagePoint;
                break;
            case Mode.Resizing:
                _target.MoveHandle(_resizeKind, imagePoint);
                break;
        }
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        try
        {
            if (_target is null) return null;

            switch (_mode)
            {
                case Mode.Moving:
                {
                    var total = new SKPoint(imagePoint.X - _moveStart.X, imagePoint.Y - _moveStart.Y);
                    // 抬手前把最新一小段 delta 补齐
                    var tail = new SKPoint(imagePoint.X - _lastMovePoint.X, imagePoint.Y - _lastMovePoint.Y);
                    if (tail.X != 0 || tail.Y != 0) _target.MoveBy(tail);

                    if (total.X == 0 && total.Y == 0) return null;

                    // 回滚到 pre-drag，再 Execute 让 command.Do 重新应用
                    _target.MoveBy(new SKPoint(-total.X, -total.Y));
                    _history.Execute(new MoveAnnotationCommand(_target, total));
                    return null; // SelectTool 从不"新增"标注
                }
                case Mode.Resizing:
                {
                    // imagePoint 是最终 handle 目标位置
                    if (imagePoint == _resizeBefore) return null;

                    // 回滚：Execute 里 Do 会重新拖到 imagePoint
                    _target.MoveHandle(_resizeKind, _resizeBefore);
                    _history.Execute(new ResizeAnnotationCommand(_target, _resizeKind, _resizeBefore, imagePoint));
                    return null;
                }
            }
            return null;
        }
        finally
        {
            _mode = Mode.Idle;
            _target = null;
        }
    }

    public void Cancel()
    {
        if (_target is not null)
        {
            switch (_mode)
            {
                case Mode.Moving:
                    var totalDelta = new SKPoint(_lastMovePoint.X - _moveStart.X, _lastMovePoint.Y - _moveStart.Y);
                    if (totalDelta.X != 0 || totalDelta.Y != 0)
                        _target.MoveBy(new SKPoint(-totalDelta.X, -totalDelta.Y));
                    break;
                case Mode.Resizing:
                    _target.MoveHandle(_resizeKind, _resizeBefore);
                    break;
            }
        }
        _mode = Mode.Idle;
        _target = null;
    }

    private AnnotationHandle? HitTestHandle(AnnotationBase annotation, SKPoint imagePoint)
    {
        var handles = annotation.GetHandles();
        var rSq = _handleHitRadius * _handleHitRadius;
        foreach (var h in handles)
        {
            var dx = imagePoint.X - h.ImagePos.X;
            var dy = imagePoint.Y - h.ImagePos.Y;
            if (dx * dx + dy * dy <= rSq) return h;
        }
        return null;
    }
}
