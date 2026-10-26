using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 马赛克：把 <see cref="PixelEffectAnnotation.Region"/> 按 <see cref="BlockSize"/> 等比降采样到
/// 小 surface，再用最近邻采样拉伸回原尺寸——块内像素一致，呈典型像素化观感。
/// 降采样结果按 (Region, BlockSize) 缓存；Source 会话内不可变，缓存安全。
/// </summary>
public sealed class MosaicAnnotation : PixelEffectAnnotation
{
    public const int DefaultBlockSize = 12;
    public const int MinBlockSize = 4;
    public const int MaxBlockSize = 48;

    private int _blockSize;
    private SKImage? _cached;
    private SKRect _cachedRegion;
    private int _cachedBlock;
    private SKRect _cachedClamped;

    public int BlockSize
    {
        get => _blockSize;
        set => _blockSize = Math.Clamp(value, MinBlockSize, MaxBlockSize);
    }

    public MosaicAnnotation(SKRect region, SKBitmap source, AnnotationStyle style, int blockSize = DefaultBlockSize)
        : base(region, source, style)
    {
        _blockSize = Math.Clamp(blockSize, MinBlockSize, MaxBlockSize);
    }

    public override void Render(SKCanvas canvas)
    {
        if (!TryClampRegionToSource(Region, out var clamped)) return;

        if (_cached is null || _cachedRegion != Region || _cachedBlock != _blockSize || _cachedClamped != clamped)
        {
            _cached?.Dispose();
            _cached = BuildDownsampled(clamped);
            _cachedRegion = Region;
            _cachedBlock = _blockSize;
            _cachedClamped = clamped;
        }

        if (_cached is null) return;

        // 最近邻放大：default(SKSamplingOptions) 全 false = nearest，块内像素完全一致
        canvas.DrawImage(_cached, new SKRect(0, 0, _cached.Width, _cached.Height), clamped, default(SKSamplingOptions));
    }

    /// <summary>降采样到 ceil(w/block) × ceil(h/block) 的小图。</summary>
    private SKImage BuildDownsampled(SKRect clamped)
    {
        var smallW = Math.Max(1, (int)MathF.Ceiling(clamped.Width / _blockSize));
        var smallH = Math.Max(1, (int)MathF.Ceiling(clamped.Height / _blockSize));

        var info = new SKImageInfo(smallW, smallH, _source.ColorType, _source.AlphaType);
        using var surface = SKSurface.Create(info);
        if (surface is null) return SKImage.Create(new SKImageInfo(1, 1));

        // 线性采样做缩小（块内取平均，抗锯齿），放大用最近邻——像素块边缘干净。
        // 3.119 的 sampling 重载只在 DrawImage 上：用 FromPixels 非拥有包装 _source。
        using var srcView = SKImage.FromPixels(_source.Info, _source.GetPixels(), _source.RowBytes);
        if (srcView is null) return SKImage.Create(new SKImageInfo(1, 1));
        surface.Canvas.DrawImage(srcView, clamped, new SKRect(0, 0, smallW, smallH),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }
}
