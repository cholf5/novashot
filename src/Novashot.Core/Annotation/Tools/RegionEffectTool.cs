using SkiaSharp;
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.Tools;

/// <summary>
/// 拖矩形区域工具（Mosaic / Blur 共享）：与 <see cref="RectangleTool"/> 同样的
/// 两角拖拽语义，但由工厂造出任意 <see cref="PixelEffectAnnotation"/> 子类。
/// 预览与提交是不同实例（拖拽中不断重建），渲染层每次现算——拖拽区域通常很小，可接受。
/// </summary>
public sealed class RegionEffectTool : IAnnotationTool
{
    /// <summary>提交阈值：宽或高 &lt; 4 px 视作误触。</summary>
    public const float MinAreaPx = 4f;

    private readonly Func<SKRect, AnnotationStyle, PixelEffectAnnotation> _factory;

    private SKPoint _anchor;
    private AnnotationStyle? _style;
    private bool _dragging;

    public AnnotationBase? Preview { get; private set; }

    public RegionEffectTool(Func<SKRect, AnnotationStyle, PixelEffectAnnotation> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _anchor = imagePoint;
        _style = style;
        _dragging = true;
        Preview = _factory(new SKRect(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y), style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) return;
        Preview = _factory(new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y), _style);
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
        Preview = null;

        var rect = new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y).Standardized;
        if (rect.Width < MinAreaPx || rect.Height < MinAreaPx) return null;
        return _factory(rect, _style);
    }

    public void Cancel()
    {
        _dragging = false;
        Preview = null;
    }
}
