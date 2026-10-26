using Prtsc.Core.Annotation.Editing;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 采样型区域效果标注基类（Mosaic / Blur 共享）：持有一个矩形 <see cref="Region"/>，
/// <see cref="Render"/> 时对 <see cref="_source"/>（<b>非拥有</b>，即 <c>AnnotationDocument.Source</c>）
/// 的该区域现场采样并施加效果，画回同一位置。
///
/// 与总体方案最初设想的"栅格化烤入图像副本 + 差分位图 Undo"不同：效果作为普通矢量式标注
/// 参与渲染管线，Undo/Redo/移动/缩放直接复用既有命令与 <see cref="BoxHandles"/>，无需位图差分。
/// Source 在编辑器会话内不可变，因此子类的采样缓存以 (Region, 参数) 为 key 是安全的。
/// </summary>
public abstract class PixelEffectAnnotation : AnnotationBase
{
    /// <summary>采样源（非拥有）。像素尺寸即文档宽高。</summary>
    protected readonly SKBitmap _source;

    private SKRect _region;
    public SKRect Region
    {
        get => _region;
        set => _region = value.Standardized;
    }

    protected PixelEffectAnnotation(SKRect region, SKBitmap source, AnnotationStyle style) : base(style)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _region = region.Standardized;
    }

    public override SKRect BoundingBox
    {
        get
        {
            var r = _region;
            return new SKRect(r.Left - 1f, r.Top - 1f, r.Right + 1f, r.Bottom + 1f);
        }
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        var r = _region;
        r.Inflate(slop, slop);
        return r.Contains(point);
    }

    /// <summary>效果覆盖的是一块实心区域：区域内任意点都算 body 命中。</summary>
    public override bool HitTestBody(SKPoint point, float slop) => HitTest(point, slop);

    public override IReadOnlyList<AnnotationHandle> GetHandles() => BoxHandles.GetHandles(_region);

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
        => _region = BoxHandles.ApplyHandle(_region, kind, newImagePos).Standardized;

    public override void MoveBy(SKPoint delta) => _region.Offset(delta.X, delta.Y);

    /// <summary>Region 与位图边界的交集（采样时不越界）。空交集返回 false。</summary>
    protected bool TryClampRegionToSource(SKRect region, out SKRect clamped)
    {
        var src = new SKRect(0, 0, _source.Width, _source.Height);
        clamped = SKRect.Intersect(region, src);
        return !clamped.IsEmpty && clamped.Width >= 1f && clamped.Height >= 1f;
    }
}
