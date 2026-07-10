using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 从 <see cref="AnnotationDocument"/> 移除一个标注。构造时捕获当前 z-order 索引，
/// Undo 时用 <see cref="AnnotationDocument.Insert"/> 精确恢复位置。
/// </summary>
public sealed class RemoveAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationDocument _document;
    private readonly AnnotationBase _annotation;
    private readonly int _index;

    public string Description => $"Remove {_annotation.GetType().Name}";

    public RemoveAnnotationCommand(AnnotationDocument document, AnnotationBase annotation)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _index = document.IndexOf(annotation);
        if (_index < 0)
            throw new ArgumentException("Annotation is not in the document.", nameof(annotation));
    }

    public void Do()
    {
        _document.Remove(_annotation);
    }

    public void Undo()
    {
        // 若栈头部已经被 clamp 到当前长度，Insert 会容忍 index == Count 追加到末尾
        var idx = Math.Min(_index, _document.Annotations.Count);
        _document.Insert(idx, _annotation);
    }
}
