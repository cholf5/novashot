using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 荧光笔手绘：宽笔画 + <see cref="SKBlendMode.Multiply"/>——叠画处逐次压深底色，
/// 视觉上是"透底"的马克笔而不是不透明刷子（Flameshot 同款观感）。
/// 宽度在 <see cref="Style.StrokeWidth"/> 之上加固定加成，让默认 2px 的全局样式也能画出粗笔触。
/// </summary>
public sealed class HighlighterAnnotation : FreehandAnnotationBase
{
    /// <summary>相对 Style.StrokeWidth 的宽度加成（px）。Ctrl+滚轮仍可继续调 StrokeWidth。</summary>
    public const float WidthBonus = 14f;

    public HighlighterAnnotation(IEnumerable<SKPoint> points, AnnotationStyle style) : base(points, style)
    {
    }

    protected override float EffectiveStrokeWidth => Style.StrokeWidth + WidthBonus;

    public override void Render(SKCanvas canvas)
    {
        if (Points.Count == 0) return;
        using var path = BuildPath();
        using var paint = new SKPaint
        {
            Color = Style.Stroke,
            StrokeWidth = Style.StrokeWidth + WidthBonus,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Round,
            BlendMode = SKBlendMode.Multiply,
        };
        canvas.DrawPath(path, paint);
    }
}
