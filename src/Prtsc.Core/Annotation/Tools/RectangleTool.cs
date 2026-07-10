using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 矩形描边工具。拖拽两点即两个对角。面积 &lt; <see cref="MinAreaPx"/> 判定为退化。
/// </summary>
public sealed class RectangleTool : IAnnotationTool
{
    /// <summary>提交阈值：宽或高 &lt; 2 px 视作误触，不落入文档。</summary>
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
        Preview = new RectangleAnnotation(
            new SKRect(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y),
            style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) return;
        Preview = new RectangleAnnotation(
            new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y),
            _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging || _style is null)
        {
            _dragging = false;
            Preview = null;
            return null;
        }

        _dragging = false;
        var rect = new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y).Standardized;
        Preview = null;

        if (rect.Width < MinAreaPx || rect.Height < MinAreaPx)
            return null;

        return new RectangleAnnotation(rect, _style);
    }

    public void Cancel()
    {
        _dragging = false;
        Preview = null;
    }
}
