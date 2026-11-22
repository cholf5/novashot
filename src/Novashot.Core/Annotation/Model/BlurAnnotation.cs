using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 高斯模糊：把 <see cref="PixelEffectAnnotation.Region"/>（外扩 3σ apron 供滤波取样）以
/// <see cref="SKImageFilter"/> 模糊后画回原位，clip 在 Region 内——区域外像素不受影响，
/// 区域边缘的模糊能采样到邻近原始像素。
/// </summary>
public sealed class BlurAnnotation : PixelEffectAnnotation
{
    public const float DefaultSigma = 8f;
    public const float MinSigma = 2f;
    public const float MaxSigma = 40f;

    private float _sigma;
    public float Sigma
    {
        get => _sigma;
        set => _sigma = Math.Clamp(value, MinSigma, MaxSigma);
    }

    public BlurAnnotation(SKRect region, SKBitmap source, AnnotationStyle style, float sigma = DefaultSigma)
        : base(region, source, style)
    {
        _sigma = Math.Clamp(sigma, MinSigma, MaxSigma);
    }

    public override void Render(SKCanvas canvas)
    {
        if (!TryClampRegionToSource(Region, out var clamped)) return;

        // 外扩 apron：让 clip 边缘处的模糊核能取到区域外的原始像素
        var apron = _sigma * 3f;
        var sample = clamped;
        sample.Inflate(apron, apron);
        if (!TryClampRegionToSource(sample, out sample)) sample = clamped;

        var restore = canvas.Save();
        canvas.ClipRect(clamped, antialias: false);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(_sigma, _sigma),
            IsAntialias = true,
        };
        // dest == src（1:1 回到原位）；paint 的 filter 只作用于这次绘制
        canvas.DrawBitmap(_source, sample, sample, paint);
        canvas.RestoreToCount(restore);
    }
}
