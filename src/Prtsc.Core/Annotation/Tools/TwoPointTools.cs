using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 两点拖拽工具的通用基类：down = 起点，drag = 更新终点，up = 若长度 &ge; 阈值则由派生类工厂造出标注。
/// 用于 <see cref="LineTool"/> / <see cref="ArrowStraightTool"/> / <see cref="ArrowCurvedTool"/>。
/// </summary>
public abstract class TwoPointToolBase : IAnnotationTool
{
    /// <summary>提交阈值：起终点距离 &lt; 2 px 视作误触。</summary>
    public const float MinLengthPx = 2f;

    private SKPoint _start;
    private AnnotationStyle? _style;
    private bool _dragging;

    public AnnotationBase? Preview { get; private set; }

    protected abstract AnnotationBase CreateAnnotation(SKPoint start, SKPoint end, AnnotationStyle style);

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _start = imagePoint;
        _style = style;
        _dragging = true;
        Preview = CreateAnnotation(imagePoint, imagePoint, style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) return;
        Preview = CreateAnnotation(_start, imagePoint, _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging || _style is null) { _dragging = false; Preview = null; return null; }
        _dragging = false;
        Preview = null;
        var dx = imagePoint.X - _start.X;
        var dy = imagePoint.Y - _start.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) < MinLengthPx) return null;
        return CreateAnnotation(_start, imagePoint, _style);
    }

    public void Cancel()
    {
        _dragging = false;
        Preview = null;
    }
}

public sealed class LineTool : TwoPointToolBase
{
    protected override AnnotationBase CreateAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        => new LineAnnotation(start, end, style);
}

public sealed class ArrowStraightTool : TwoPointToolBase
{
    protected override AnnotationBase CreateAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        => new ArrowStraightAnnotation(start, end, style);
}

public sealed class ArrowCurvedTool : TwoPointToolBase
{
    protected override AnnotationBase CreateAnnotation(SKPoint start, SKPoint end, AnnotationStyle style)
        => new ArrowCurvedAnnotation(start, end, style);
}
