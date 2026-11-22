using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.History;

/// <summary>
/// 向 <see cref="AnnotationDocument"/> 追加一个标注的可撤销命令。
/// 撤销时移除同一实例；重做时再次添加同一实例（Guid 稳定）。
/// </summary>
public sealed class AddAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationDocument _document;
    private readonly AnnotationBase _annotation;

    public string Description => $"Add {_annotation.GetType().Name}";

    public AddAnnotationCommand(AnnotationDocument document, AnnotationBase annotation)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
    }

    public void Do() => _document.Add(_annotation);

    public void Undo() => _document.Remove(_annotation);
}
