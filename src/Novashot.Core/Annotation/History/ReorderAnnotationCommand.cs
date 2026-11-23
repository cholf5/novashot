using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.History;

/// <summary>
/// 调整标注的 z-order。构造时捕获 from 索引，Do = Move(from, to)，Undo = Move(to, from)，
/// 语义幂等。<paramref name="annotation"/> 必须已在文档内（IndexOf &gt;= 0，否则抛）。
///
/// 注意：若中间隔着其它改 z-order 的操作，索引可能漂移——与 RemoveAnnotationCommand
/// 的 z-index 捕获同款限制。
/// </summary>
public sealed class ReorderAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationDocument _document;
    private readonly AnnotationBase _annotation;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public string Description => $"Reorder {_annotation.GetType().Name} {_fromIndex} -> {_toIndex}";

    public ReorderAnnotationCommand(AnnotationDocument document, AnnotationBase annotation, int toIndex)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _fromIndex = document.IndexOf(annotation);
        ArgumentOutOfRangeException.ThrowIfNegative(_fromIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(toIndex, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, document.Annotations.Count);
        _toIndex = toIndex;
    }

    public void Do() => _document.Move(_fromIndex, _toIndex);

    public void Undo() => _document.Move(_toIndex, _fromIndex);
}
