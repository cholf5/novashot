using SkiaSharp;
using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 自由手绘工具基类：down 落第一个点建 annotation，move 以最小步长去抖后追加，
/// up 按累计路径长度判定是否提交。预览与提交是<b>同一实例</b>（就地生长，与 SelectTool 的
/// 原地修改哲学一致）。
/// </summary>
public abstract class FreehandToolBase<T> : IAnnotationTool where T : FreehandAnnotationBase
{
    /// <summary>相邻记录点的最小间距（px）：抑制指针抖动产生的密集冗余点。</summary>
    public const float MinStepPx = 1.5f;

    /// <summary>提交阈值：累计路径长度 &lt; 2 px（原地单击/极慢微动）视作误触。</summary>
    public const float MinPathLengthPx = 2f;

    private T? _annotation;
    private bool _dragging;

    public AnnotationBase? Preview => _annotation;

    protected abstract T CreateAnnotation(SKPoint firstPoint, AnnotationStyle style);

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _dragging = true;
        _annotation = CreateAnnotation(imagePoint, style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging || _annotation is null) return;
        var pts = _annotation.Points;
        if (pts.Count == 0)
        {
            _annotation.AppendPoint(imagePoint);
            return;
        }
        var last = pts[^1];
        var dx = imagePoint.X - last.X;
        var dy = imagePoint.Y - last.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) >= MinStepPx)
            _annotation.AppendPoint(imagePoint);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging || _annotation is null)
        {
            _dragging = false;
            _annotation = null;
            return null;
        }
        _dragging = false;
        var result = _annotation;
        _annotation = null;

        // 抬手位置距最后记录点不足一步时补记，保证结尾不丢
        if (result.Points.Count > 0)
        {
            var last = result.Points[^1];
            var dx = imagePoint.X - last.X;
            var dy = imagePoint.Y - last.Y;
            if (MathF.Sqrt(dx * dx + dy * dy) >= MinStepPx)
                result.AppendPoint(imagePoint);
        }

        if (result.Points.Count < 2) return null;
        if (PolylineGeometry.PathLength(result.Points) < MinPathLengthPx) return null;
        return result;
    }

    public void Cancel()
    {
        _dragging = false;
        _annotation = null;
    }
}

/// <summary>铅笔：普通实线手绘。</summary>
public sealed class PencilTool : FreehandToolBase<PencilAnnotation>
{
    protected override PencilAnnotation CreateAnnotation(SKPoint firstPoint, AnnotationStyle style)
        => new(new[] { firstPoint }, style);
}

/// <summary>荧光笔：Multiply 混合的宽笔触手绘。</summary>
public sealed class HighlighterTool : FreehandToolBase<HighlighterAnnotation>
{
    protected override HighlighterAnnotation CreateAnnotation(SKPoint firstPoint, AnnotationStyle style)
        => new(new[] { firstPoint }, style);
}
