using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 文本标注。<see cref="Position"/> 是文字左下基线锚点（SKCanvas.DrawText 语义）。
/// M4b 基础版：一次点击落一个占位文本 <c>"Text"</c>；内联编辑 UI 在 M4c 补齐（SelectTool + 属性面板）。
/// </summary>
public sealed class TextAnnotation : AnnotationBase
{
    /// <summary>默认字体家族（Windows 常驻）。M6 起可通过属性面板改。</summary>
    public const string DefaultFontFamily = "Segoe UI";

    public SKPoint Position { get; set; }

    private string _text;
    public string Text
    {
        get => _text;
        set { _text = value ?? string.Empty; InvalidateCache(); }
    }

    private float _fontSize;
    public float FontSize
    {
        get => _fontSize;
        set { _fontSize = value; InvalidateCache(); }
    }

    public string FontFamily { get; set; }

    private SKRect? _cachedBounds;
    private void InvalidateCache() => _cachedBounds = null;

    public TextAnnotation(SKPoint position, string text, float fontSize, AnnotationStyle style,
                          string? fontFamily = null) : base(style)
    {
        Position = position;
        _text = text ?? string.Empty;
        _fontSize = fontSize;
        FontFamily = fontFamily ?? DefaultFontFamily;
    }

    private SKRect Measure()
    {
        if (_cachedBounds is { } cached) return cached;
        using var typeface = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        using var font = new SKFont(typeface, _fontSize);
        var bounds = new SKRect();
        if (!string.IsNullOrEmpty(_text))
            font.MeasureText(_text.AsSpan(), out bounds);
        // 平移到 Position（MeasureText 返回相对 baseline 的框）
        bounds.Offset(Position.X, Position.Y);
        _cachedBounds = bounds;
        return bounds;
    }

    public override SKRect BoundingBox
    {
        get
        {
            var m = Measure();
            var pad = 2f;
            m.Inflate(pad, pad);
            return m;
        }
    }

    public override void Render(SKCanvas canvas)
    {
        if (string.IsNullOrEmpty(_text)) return;
        using var typeface = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        using var font = new SKFont(typeface, _fontSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint
        {
            Color = Style.Stroke,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawText(_text, Position.X, Position.Y, SKTextAlign.Left, font, paint);
    }

    public override bool HitTest(SKPoint point, float slop)
    {
        var b = Measure();
        b.Inflate(slop, slop);
        return b.Contains(point);
    }
}
