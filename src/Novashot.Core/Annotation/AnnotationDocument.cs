using SkiaSharp;
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation;

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

    private SKImage? _renderImage;

    /// <summary>
    /// 源图的渲染视图（懒创建，<see cref="SKImage.FromBitmap"/> 会拷贝像素、自持有内存，
    /// 与 Source 的释放顺序解耦）。必须整个会话复用同一实例：GPU 画布的纹理/mipmap
    /// 缓存按 SKImage 实例生效，每帧新建包装会在 4K 源图上逐帧重传。
    /// </summary>
    public SKImage RenderImage => _renderImage ??= SKImage.FromBitmap(Source);

    /// <summary>释放渲染视图缓存。拥有 Source 的一方在释放 Source 前调用；不调用则由 GC 兜底。</summary>
    public void ReleaseRenderImage()
    {
        _renderImage?.Dispose();
        _renderImage = null;
    }

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

    /// <summary>
    /// 在指定 z-order 位置插入。<paramref name="index"/> 允许等于当前长度（追加到末尾）。
    /// 由 <c>RemoveAnnotationCommand.Undo</c> 用来恢复原来的层级顺序。
    /// </summary>
    public void Insert(int index, AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (index < 0 || index > _annotations.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _annotations.Insert(index, annotation);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>返回 annotation 在栈里的 z-order 索引；找不到返回 -1。</summary>
    public int IndexOf(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return _annotations.IndexOf(annotation);
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

    /// <summary>
    /// 通告"某个已存在的标注内部状态发生了变化"（Move/Resize/Style/Text 等原地改动）。
    /// 集合大小不变，所以既有的 Add/Remove/Insert 不会触发；命令实现方在 Do/Undo 结尾调它。
    /// SkiaCanvas 借此触发重绘。
    /// </summary>
    public void NotifyMutated()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
