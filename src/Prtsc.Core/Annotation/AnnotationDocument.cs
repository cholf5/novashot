using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation;

/// <summary>
/// 编辑器的核心数据模型：源图像 + 标注层堆。
///
/// 生命期约定：<see cref="Source"/> 是<b>非拥有</b>引用——由外层（<c>EditorViewModel</c>
/// 拥有的 <c>CapturedImage</c>）负责释放。Document 本身不实现 IDisposable。
///
/// M4a：只支持 Add / Remove / Clear。M4c 起会加 z-order 调整 / 选择集。
/// </summary>
public sealed class AnnotationDocument
{
    private readonly List<AnnotationBase> _annotations = new();

    /// <summary>源图（非拥有）。像素尺寸 = <see cref="Width"/> × <see cref="Height"/>。</summary>
    public SKBitmap Source { get; }

    public int Width => Source.Width;
    public int Height => Source.Height;

    /// <summary>只读快照。若需增删走 <see cref="Add"/> / <see cref="Remove"/> / <see cref="Clear"/>。</summary>
    public IReadOnlyList<AnnotationBase> Annotations => _annotations;

    /// <summary>Add / Remove / Clear 后触发。SkiaCanvas 订阅它触发重绘。</summary>
    public event EventHandler? Changed;

    public AnnotationDocument(SKBitmap source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Add(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        _annotations.Add(annotation);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var removed = _annotations.Remove(annotation);
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Clear()
    {
        if (_annotations.Count == 0) return;
        _annotations.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
