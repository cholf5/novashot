using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 拖动单个手柄改变形状。保存 before / after 绝对位置，
/// Do / Undo 都是"把该 handle 拖到指定位置"，语义幂等。
/// </summary>
public sealed class ResizeAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationBase _annotation;
    private readonly HandleKind _kind;
    private readonly SKPoint _before;
    private readonly SKPoint _after;

    public string Description => $"Resize {_annotation.GetType().Name}";

    public ResizeAnnotationCommand(AnnotationBase annotation, HandleKind kind, SKPoint before, SKPoint after)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _kind = kind;
        _before = before;
        _after = after;
    }

    public void Do()
    {
        _annotation.MoveHandle(_kind, _after);
    }

    public void Undo()
    {
        _annotation.MoveHandle(_kind, _before);
    }
}
