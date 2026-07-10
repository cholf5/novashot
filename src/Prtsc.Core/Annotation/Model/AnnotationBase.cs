using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

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
}
