using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 整体平移一个标注。记录 <see cref="SKPoint"/> 位移 delta，
/// Do 应用 +delta，Undo 应用 -delta；重复调用无累积误差。
///
/// 提交时机：SelectTool 在 PointerUp 时若累计位移非零才 push 到栈，避免细微抖动灌栈。
///
/// <paramref name="document"/> 可选：若给出，Do/Undo 结尾会调
/// <see cref="AnnotationDocument.NotifyMutated"/> 触发画布重绘。测试路径下可传 null。
/// </summary>
public sealed class MoveAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationBase _annotation;
    private readonly SKPoint _delta;
    private readonly AnnotationDocument? _document;

    public string Description => $"Move {_annotation.GetType().Name}";

    public MoveAnnotationCommand(AnnotationBase annotation, SKPoint delta, AnnotationDocument? document = null)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _delta = delta;
        _document = document;
    }

    public void Do()
    {
        _annotation.MoveBy(_delta);
        _document?.NotifyMutated();
    }

    public void Undo()
    {
        _annotation.MoveBy(new SKPoint(-_delta.X, -_delta.Y));
        _document?.NotifyMutated();
    }
}
