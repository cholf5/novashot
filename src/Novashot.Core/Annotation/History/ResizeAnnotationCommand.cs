using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Annotation.History;

/// <summary>
/// 拖动单个手柄改变形状。保存 before / after 绝对位置，
/// Do / Undo 都是"把该 handle 拖到指定位置"，语义幂等。
///
/// <paramref name="document"/> 可选：若给出，Do/Undo 结尾会调
/// <see cref="AnnotationDocument.NotifyMutated"/> 触发画布重绘。
/// </summary>
public sealed class ResizeAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationBase _annotation;
    private readonly HandleKind _kind;
    private readonly SKPoint _before;
    private readonly SKPoint _after;
    private readonly AnnotationDocument? _document;

    public string Description => $"Resize {_annotation.GetType().Name}";

    public ResizeAnnotationCommand(
        AnnotationBase annotation,
        HandleKind kind,
        SKPoint before,
        SKPoint after,
        AnnotationDocument? document = null)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _kind = kind;
        _before = before;
        _after = after;
        _document = document;
    }

    public void Do()
    {
        _annotation.MoveHandle(_kind, _after);
        _document?.NotifyMutated();
    }

    public void Undo()
    {
        _annotation.MoveHandle(_kind, _before);
        _document?.NotifyMutated();
    }
}
