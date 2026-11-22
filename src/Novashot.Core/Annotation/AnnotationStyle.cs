using SkiaSharp;

namespace Novashot.Core.Annotation;

/// <summary>
/// 单个标注的画笔样式。<see cref="AnnotationBase"/> 会引用一份实例，
/// 因此设计为不可变 <c>record class</c>；改样式 = new 一份。
///
/// M4a 只用 Stroke / StrokeWidth；Fill 与 Dash 已预留，M4b/M4c 逐步启用。
/// </summary>
public sealed record class AnnotationStyle(
    SKColor Stroke,
    float StrokeWidth,
    SKColor? Fill = null,
    AnnotationDashStyle Dash = AnnotationDashStyle.Solid)
{
    /// <summary>M4 默认样式：红色描边、2 像素宽、无填充、实线。</summary>
    public static readonly AnnotationStyle Default = new(
        Stroke: new SKColor(0xE5, 0x3E, 0x3E),
        StrokeWidth: 2f);
}

public enum AnnotationDashStyle
{
    Solid,
    Dashed,
    Dotted,
}
