using SkiaSharp;
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.Tools;

/// <summary>
/// 聚光灯工具：拖出高亮区，其余区域自动黑化。构造时需知道画布像素尺寸。
/// </summary>
public sealed class SpotlightTool : IAnnotationTool
{
    public const float MinAreaPx = 4f;

    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private SKPoint _anchor;
    private AnnotationStyle? _style;
    private bool _dragging;

    public AnnotationBase? Preview { get; private set; }

    public SpotlightTool(int imageWidth, int imageHeight)
    {
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        _anchor = imagePoint;
        _style = style;
        _dragging = true;
        Preview = new SpotlightAnnotation(
            new SKRect(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y),
            _imageWidth, _imageHeight, style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) return;
        Preview = new SpotlightAnnotation(
            new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y),
            _imageWidth, _imageHeight, _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) { _dragging = false; Preview = null; return null; }
        _dragging = false;
        Preview = null;
        var rect = new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y).Standardized;
        if (rect.Width < MinAreaPx || rect.Height < MinAreaPx) return null;
        return new SpotlightAnnotation(rect, _imageWidth, _imageHeight, _style);
    }

    public void Cancel()
    {
        _dragging = false;
        Preview = null;
    }
}
