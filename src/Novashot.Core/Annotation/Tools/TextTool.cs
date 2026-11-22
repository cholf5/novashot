using SkiaSharp;
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.Tools;

/// <summary>
/// 文本工具：单击落一个占位 <see cref="TextAnnotation"/>。M4c 会补内联编辑弹层。
/// </summary>
public sealed class TextTool : IAnnotationTool
{
    /// <summary>默认字号（像素空间）。M6 属性面板会开放调整。</summary>
    public const float DefaultFontSize = 24f;

    /// <summary>初始占位文本。M4c 会在落下后立即进入编辑态修改它。</summary>
    public const string PlaceholderText = "Text";

    private SKPoint _pos;
    private AnnotationStyle? _style;
    private bool _armed;

    public AnnotationBase? Preview { get; private set; }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        _pos = imagePoint;
        _style = style;
        _armed = true;
        // 落到基线下方一点，让用户点击处大致在文字中央
        var basePos = new SKPoint(imagePoint.X, imagePoint.Y + DefaultFontSize * 0.35f);
        Preview = new TextAnnotation(basePos, PlaceholderText, DefaultFontSize, style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        // 允许拖动放置位置
        if (!_armed || _style is null) return;
        _pos = imagePoint;
        var basePos = new SKPoint(imagePoint.X, imagePoint.Y + DefaultFontSize * 0.35f);
        Preview = new TextAnnotation(basePos, PlaceholderText, DefaultFontSize, _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_armed || _style is null) { _armed = false; Preview = null; return null; }
        _armed = false;
        Preview = null;
        var basePos = new SKPoint(imagePoint.X, imagePoint.Y + DefaultFontSize * 0.35f);
        return new TextAnnotation(basePos, PlaceholderText, DefaultFontSize, _style);
    }

    public void Cancel()
    {
        _armed = false;
        Preview = null;
    }
}
