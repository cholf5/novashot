using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Annotation.Model;

/// <summary>
/// 编号标注（Flameshot 的 number-counter 工具）：
/// 一个填充圆盘 + 内部居中数字。Style.Fill 决定圆盘颜色；Style.Stroke 决定数字颜色。
/// 若 Style.Fill 未指定，用 Stroke 作圆盘颜色、白色作数字色。
/// </summary>
public sealed class NumberAnnotation : AnnotationBase
{
    public const float DefaultRadiusMul = 12f; // 相对 strokeWidth
    public const float DefaultFontRatio = 1.1f; // 字体高度 / 半径

    public SKPoint Center { get; set; }
    public int Number { get; private set; }
    public float Radius { get; set; }

    public NumberAnnotation(SKPoint center, int number, AnnotationStyle style, float? radius = null)
        : base(style)
    {
        Center = center;
        Number = ClampNumber(number);
        Radius = radius ?? style.StrokeWidth * DefaultRadiusMul;
    }

    /// <summary>修改编号（Ctrl+滚轮；Flameshot circlecount 行为）。钳在 1..999。</summary>
    public void SetNumber(int value) => Number = ClampNumber(value);

    private static int ClampNumber(int value) => Math.Clamp(value, 1, 999);

    public override SKRect BoundingBox => new(
        Center.X - Radius - 1, Center.Y - Radius - 1,
        Center.X + Radius + 1, Center.Y + Radius + 1);

    public override void Render(SKCanvas canvas)
    {
        var fill = Style.Fill ?? Style.Stroke;
        var textColor = Style.Fill.HasValue ? Style.Stroke : SKColors.White;

        using (var p = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawCircle(Center, Radius, p);

        var label = Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fontSize = Radius * DefaultFontRatio;
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, fontSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };

        var textBounds = new SKRect();
        font.MeasureText(label.AsSpan(), out textBounds);
        // 视觉居中：以 bounds 中点对齐 Center
        var x = Center.X - (textBounds.Left + textBounds.Right) * 0.5f;
        var y = Center.Y - (textBounds.Top + textBounds.Bottom) * 0.5f;
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, textPaint);
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        return dx * dx + dy * dy <= (Radius + slop) * (Radius + slop);
    }

    public override IReadOnlyList<AnnotationHandle> GetHandles() => new[]
    {
        new AnnotationHandle(HandleKind.Center, Center),
    };

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        if (kind == HandleKind.Center) Center = newImagePos;
    }

    public override void MoveBy(SKPoint delta)
    {
        Center = new SKPoint(Center.X + delta.X, Center.Y + delta.Y);
    }
}
