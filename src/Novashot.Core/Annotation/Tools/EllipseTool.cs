using SkiaSharp;
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.Tools;

/// <summary>椭圆：与 <see cref="RectangleTool"/> 同交互，拖拽两对角。</summary>
public sealed class EllipseTool : IAnnotationTool
{
    public const float MinAreaPx = 2f;

    private SKPoint _anchor;
    private AnnotationStyle? _style;
    private bool _dragging;

    public AnnotationBase? Preview { get; private set; }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _anchor = imagePoint;
        _style = style;
        _dragging = true;
        Preview = new EllipseAnnotation(
            new SKRect(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y), style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) return;
        Preview = new EllipseAnnotation(
            new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y), _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) { _dragging = false; Preview = null; return null; }
        _dragging = false;
        var rect = new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y).Standardized;
        Preview = null;
        if (rect.Width < MinAreaPx || rect.Height < MinAreaPx) return null;
        return new EllipseAnnotation(rect, _style);
    }

    public void Cancel()
    {
        _dragging = false;
        Preview = null;
    }
}
