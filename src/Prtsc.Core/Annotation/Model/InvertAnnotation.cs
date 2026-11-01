using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 反色（Flameshot invert 工具）：先把 <see cref="PixelEffectAnnotation.Region"/> 的源像素画回原位，
/// 再叠一层与白色做 <see cref="SKBlendMode.Difference"/> 的矩形——<c>|dst − 255| = 255 − src</c>，
/// 等价逐通道反相。区域外不受影响；内容自包含于 <c>_source</c>，与 Mosaic/Blur 同族语义。
/// </summary>
public sealed class InvertAnnotation : PixelEffectAnnotation
{
    public InvertAnnotation(SKRect region, SKBitmap source, AnnotationStyle style)
        : base(region, source, style)
    {
    }

    public override void Render(SKCanvas canvas)
    {
        if (!TryClampRegionToSource(Region, out var clamped)) return;

        canvas.DrawBitmap(_source, clamped, clamped);
        using var paint = new SKPaint { Color = SKColors.White, BlendMode = SKBlendMode.Difference };
        canvas.DrawRect(clamped, paint);
    }
}
