using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.History;

/// <summary>
/// 改变 <see cref="TextAnnotation.Text"/>。保存 before / after 字符串，
/// Do / Undo 都是"把 Text 设为指定值"，语义幂等。
///
/// <paramref name="document"/> 可选：若给出，Do/Undo 结尾会调
/// <see cref="AnnotationDocument.NotifyMutated"/> 触发画布重绘。
/// </summary>
public sealed class ChangeTextCommand : IUndoableCommand
{
    private readonly TextAnnotation _annotation;
    private readonly string _before;
    private readonly string _after;
    private readonly AnnotationDocument? _document;

    public string Description => "Edit Text";

    public ChangeTextCommand(TextAnnotation annotation, string before, string after, AnnotationDocument? document = null)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _before = before ?? string.Empty;
        _after = after ?? string.Empty;
        _document = document;
    }

    public void Do()
    {
        _annotation.Text = _after;
        _document?.NotifyMutated();
    }

    public void Undo()
    {
        _annotation.Text = _before;
        _document?.NotifyMutated();
    }
}
