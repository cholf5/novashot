using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 聚光灯标注（WinShot/Flameshot 都有）：把整张图暗化，除 <see cref="Region"/> 之外。
/// 因为要覆盖全图，构造时需要图像尺寸；Style.Fill 决定暗化颜色（默认 rgba(0,0,0,0x99)）。
/// </summary>
public sealed class SpotlightAnnotation : AnnotationBase
{
    /// <summary>默认暗化颜色：半透明黑（60% alpha）。</summary>
    public static readonly SKColor DefaultOverlayColor = new(0, 0, 0, 0x99);

    public int ImageWidth { get; }
    public int ImageHeight { get; }

    private SKRect _region;
    public SKRect Region
    {
        get => _region;
        set => _region = value.Standardized;
    }

    public SpotlightAnnotation(SKRect region, int imageWidth, int imageHeight, AnnotationStyle style)
        : base(style)
    {
        _region = region.Standardized;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
    }

    /// <summary>此标注渲染时覆盖整张图，所以 BoundingBox 是完整画布。</summary>
    public override SKRect BoundingBox => new(0, 0, ImageWidth, ImageHeight);

    public override void Render(SKCanvas canvas)
    {
        var overlayColor = Style.Fill ?? DefaultOverlayColor;

        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(new SKRect(0, 0, ImageWidth, ImageHeight));
        path.AddRect(_region);
        using var paint = new SKPaint { Color = overlayColor, Style = SKPaintStyle.Fill, IsAntialias = false };
        canvas.DrawPath(path, paint);

        // 可选描边高亮 spotlight 边缘
        if (Style.StrokeWidth > 0)
        {
            using var stroke = new SKPaint
            {
                Color = Style.Stroke,
                StrokeWidth = Style.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            canvas.DrawRect(_region, stroke);
        }
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        // 命中"高亮区外"（点在暗化区）都算命中，方便未来点选取消
        var img = new SKRect(0, 0, ImageWidth, ImageHeight);
        if (!img.Contains(point)) return false;
        return !_region.Contains(point);
    }

    public override IReadOnlyList<AnnotationHandle> GetHandles() => BoxHandles.GetHandles(_region);

    public override bool HitTestBody(SKPoint point, float slop)
    {
        // Select 工具下，聚光灯覆盖整张图，任意位置都算命中。
        var img = new SKRect(0, 0, ImageWidth, ImageHeight);
        img.Inflate(slop, slop);
        return img.Contains(point);
    }

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        _region = BoxHandles.ApplyHandle(_region, kind, newImagePos).Standardized;
    }

    public override void MoveBy(SKPoint delta)
    {
        _region.Offset(delta.X, delta.Y);
    }
}
