using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 所有标注对象的抽象基类。坐标一律在源图像 <b>像素空间</b>；
/// SkiaCanvas 只做视口 (fit-to-bounds) 变换，不介入标注自身坐标。
///
/// 每个具体标注实现 <see cref="Render"/> 与 <see cref="HitTest"/>；
/// <see cref="BoundingBox"/> 用于 dirty 区计算 / 选择框对齐。
/// </summary>
public abstract class AnnotationBase
{
    public Guid Id { get; } = Guid.NewGuid();

    public AnnotationStyle Style { get; set; }

    protected AnnotationBase(AnnotationStyle style)
    {
        Style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>该标注的紧致 AABB，像素坐标。用于 dirty / 命中前置过滤。</summary>
    public abstract SKRect BoundingBox { get; }

    /// <summary>把自己画到给定 SKCanvas。canvas 处于 image-pixel 坐标系。</summary>
    public abstract void Render(SKCanvas canvas);

    /// <summary>
    /// 点是否命中本标注（含描边）。<paramref name="slop"/> 是拾取容差（像素），
    /// 让描边细的标注也易点中。
    /// </summary>
    public abstract bool HitTest(SKPoint point, float slop);

    /// <summary>
    /// Select 工具专用的宽松 body 命中：<b>闭合形状</b>（Rect/Ellipse/Spotlight/Text/Number）
    /// 应把整个 body 内部都算命中，用户不必去精确点描边；开放形状（Line/Arrow）沿用
    /// <see cref="HitTest"/> 的描边命中。默认走 <see cref="HitTest"/>。
    /// </summary>
    public virtual bool HitTestBody(SKPoint point, float slop) => HitTest(point, slop);

    /// <summary>选中态可拖手柄集合，坐标为像素空间；顺序建议与视觉迭代顺序一致。</summary>
    public abstract IReadOnlyList<AnnotationHandle> GetHandles();

    /// <summary>把指定 <paramref name="kind"/> 手柄移动到 <paramref name="newImagePos"/>。未知 kind 不动。</summary>
    public abstract void MoveHandle(HandleKind kind, SKPoint newImagePos);

    /// <summary>整体平移。<paramref name="delta"/> 单位是像素空间。</summary>
    public abstract void MoveBy(SKPoint delta);
}
