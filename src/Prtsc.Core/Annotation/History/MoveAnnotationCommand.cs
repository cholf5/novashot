using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 整体平移一个标注。记录 <see cref="SKPoint"/> 位移 delta，
/// Do 应用 +delta，Undo 应用 -delta；重复调用无累积误差。
///
/// 提交时机：SelectTool 在 PointerUp 时若累计位移非零才 push 到栈，避免细微抖动灌栈。
/// </summary>
public sealed class MoveAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationBase _annotation;
    private readonly SKPoint _delta;

    public string Description => $"Move {_annotation.GetType().Name}";

    public MoveAnnotationCommand(AnnotationBase annotation, SKPoint delta)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _delta = delta;
    }

    public void Do()
    {
        _annotation.MoveBy(_delta);
    }

    public void Undo()
    {
        _annotation.MoveBy(new SKPoint(-_delta.X, -_delta.Y));
    }
}
