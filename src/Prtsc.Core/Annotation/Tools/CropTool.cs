using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 非破坏裁剪工具（M6d）：两角拖拽出裁剪框。与标注工具不同——
/// 不产出 <see cref="AnnotationBase"/>（<see cref="OnPointerUp"/> 恒为 null），
/// 拖拽结果经 <see cref="Committed"/> 交给 EditorViewModel 写入 EditorState.CropRect
/// （走 OnCompositionStateChanged 通路，不进 Undo）。
/// 预览矩形由 SkiaCanvas 画 overlay（暗遮罩 + 白框），不进导出。
/// </summary>
public sealed class CropTool : IAnnotationTool
{
    /// <summary>提交阈值：宽或高 &lt; 4 px 视作误触。</summary>
    public const float MinAreaPx = 4f;

    private readonly SKRect _imageBounds;
    private SKPoint _anchor;
    private bool _dragging;

    /// <summary>拖拽中的预览矩形（image 空间）；未拖拽时 null。</summary>
    public SKRect? PreviewRect { get; private set; }

    /// <summary>本次拖拽提交的裁剪框（已钳回图像内）；误触或取消为 null。</summary>
    public SKRect? Committed { get; private set; }

    public CropTool(SKRect imageBounds)
    {
        _imageBounds = imageBounds;
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        _anchor = imagePoint;
        _dragging = true;
        PreviewRect = new SKRect(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_dragging) return;
        PreviewRect = new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_dragging)
        {
            PreviewRect = null;
            return null;
        }
        _dragging = false;
        PreviewRect = null;

        var rect = CropRects.ClampToImage(
            new SKRect(_anchor.X, _anchor.Y, imagePoint.X, imagePoint.Y), _imageBounds);
        if (rect is not { } r || r.Width < MinAreaPx || r.Height < MinAreaPx)
        {
            Committed = null;
            return null;
        }
        Committed = r;
        return null; // 不产出标注
    }

    public void Cancel()
    {
        _dragging = false;
        PreviewRect = null;
        Committed = null;
    }

    /// <summary>IAnnotationTool.Preview：裁剪预览不渲染为标注（overlay 由画布画），恒为 null。</summary>
    public AnnotationBase? Preview => null;

    /// <summary>把裁剪框写入 EditorState（null = 撤销裁剪）。</summary>
    public static EditorState AppliedToState(EditorState state, SKRect? cropRect)
        => state with { CropRect = cropRect };
}
