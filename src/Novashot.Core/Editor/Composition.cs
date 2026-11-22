using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Utils;
using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// 文档内容的合成渲染：源图 + 标注层（z 序），画到 image-pixel 空间的 canvas 上。
/// 这是<b>唯一</b>的文档内容渲染事实来源——编辑器（SkiaCanvas）与导出（ImageExporter）
/// 都经过它，保证"所见即所得"；M6b 会在此之上扩展 EditorState（背景/圆角/阴影/边框）。
///
/// 注意本类只画<b>文档内容</b>：暗色背景板、视口矩阵、选中框/手柄等编辑器 chrome
/// 不在此处，永不进入导出。
/// </summary>
public static class Composition
{
    /// <summary>
    /// 把 <paramref name="document"/> 的内容渲染到 canvas（坐标 = 源图像素空间）。
    /// </summary>
    /// <param name="canvas">目标 canvas（编辑器态已 concat 视口矩阵；导出态为离屏 1:1）。</param>
    /// <param name="document">文档（源图 + 标注堆）。</param>
    /// <param name="preview">拖拽中的预览标注，叠最顶层；编辑器态用，导出传 null。</param>
    /// <param name="skipAnnotation">需要跳过的标注（内联编辑中的 Text——画布渲染时它由
    /// 浮动 TextBox 代替；导出传 null）。</param>
    public static void RenderScene(SKCanvas canvas, AnnotationDocument document,
        AnnotationBase? preview = null, AnnotationBase? skipAnnotation = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(document);

        // 源图平滑重采样（口径统一在 Utils.Sampling）：预览视口几乎总是非 1:1，
        // DrawBitmap 默认 nearest 会嚼出摩尔纹/马赛克，见 Sampling.cs 备注。
        canvas.DrawImage(document.RenderImage, 0, 0, Sampling.Smooth, null);
        foreach (var annotation in document.Annotations)
        {
            if (skipAnnotation is not null && ReferenceEquals(annotation, skipAnnotation)) continue;
            annotation.Render(canvas);
        }
        preview?.Render(canvas);
    }

    /// <summary>合成输出尺寸 = 裁剪尺寸（无 crop 时全图）+ 2×Padding。</summary>
    public static SKSizeI Measure(AnnotationDocument document, EditorState state)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        var s = state.Clamped(new SKSizeI(document.Width, document.Height));
        var (cw, ch) = CroppedSize(document, s);
        return new SKSizeI(cw + 2 * s.Padding, ch + 2 * s.Padding);
    }

    private static (int Width, int Height) CroppedSize(AnnotationDocument document, EditorState s)
    {
        if (s.CropRect is { } crop)
        {
            var size = CropRects.ClampToImage(crop, new SKRect(0, 0, document.Width, document.Height));
            if (size is { } c)
                return ((int)MathF.Round(c.Width), (int)MathF.Round(c.Height));
        }
        return (document.Width, document.Height);
    }

    /// <summary>
    /// 带合成状态（背景/padding/圆角/阴影/内衬/边框）的完整管线，画在<b>内容空间</b>
    /// （左上角 = 内容原点，图像位于 (Padding, Padding)）：
    /// 背景 → 阴影 → clip 圆角 + 平移后的文档内容 → 边框 → 内衬。
    /// 默认 <see cref="EditorState.Default"/> 时与四参 <see cref="RenderScene(SKCanvas, AnnotationDocument, AnnotationBase?, AnnotationBase?)"/>
    /// 逐像素等价。
    /// </summary>
    public static void RenderScene(SKCanvas canvas, AnnotationDocument document, EditorState state,
        AnnotationBase? preview = null, AnnotationBase? skipAnnotation = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);

        var s = state.Clamped(new SKSizeI(document.Width, document.Height));
        var size = Measure(document, s);
        var contentRect = new SKRect(0, 0, size.Width, size.Height);
        var (contentW, contentH) = CroppedSize(document, s);
        var imageRect = new SKRect(
            s.Padding, s.Padding,
            s.Padding + contentW, s.Padding + contentH);
        // 无 crop 时 cropOrigin = (0,0)，与既有路径一致
        var cropOrigin = s.CropRect is { } crop
            ? new SKPoint(crop.Left, crop.Top)
            : new SKPoint(0, 0);

        // 1. 背景
        DrawBackground(canvas, s, contentRect);

        // 2. 阴影（画在图像之下，中心被图像盖住，只露出四周 blur）
        if (s.ShadowSize > 0)
        {
            using var shadowPaint = new SKPaint
            {
                Color = EditorState.ShadowColor,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(s.ShadowSize, s.ShadowSize),
            };
            canvas.DrawRoundRect(new SKRoundRect(imageRect, s.CornerRadius), shadowPaint);
        }

        // 3. clip 圆角 → 平移到图像原点 → 平移掉 crop 原点 → 文档内容（源图 + 标注 z 序）。
        // crop 时画布只露出 crop 矩形（标注越界部分被剪掉，非破坏）。
        var restore = canvas.Save();
        if (s.CornerRadius > 0)
            canvas.ClipRoundRect(new SKRoundRect(imageRect, s.CornerRadius), antialias: true);
        else
            canvas.ClipRect(imageRect, antialias: true);
        canvas.Translate(s.Padding, s.Padding);
        if (s.CropRect is not null)
        {
            canvas.ClipRect(new SKRect(0, 0, contentW, contentH), antialias: false);
            canvas.Translate(-cropOrigin.X, -cropOrigin.Y);
        }
        RenderScene(canvas, document, preview, skipAnnotation);
        canvas.RestoreToCount(restore);

        // 4. 边框（Outside: 外扩 w/2；Center: 贴边；Inside: 内缩 w/2）
        if (s.BorderWidth > 0)
        {
            var half = s.BorderWidth * 0.5f;
            var (rect, radius) = s.Border switch
            {
                BorderStyle.Outside => (Inflate(imageRect, half), s.CornerRadius + half),
                BorderStyle.Inside  => (Inflate(imageRect, -half), MathF.Max(0f, s.CornerRadius - half)),
                _                   => (imageRect, s.CornerRadius),
            };
            using var borderPaint = new SKPaint
            {
                Color = s.EffectiveBorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = s.BorderWidth,
                IsAntialias = true,
            };
            DrawRoundedOrRect(canvas, rect, radius, borderPaint);
        }

        // 5. 内衬（图像边缘向内的等宽色环）
        if (s.InsetSize > 0)
        {
            var half = s.InsetSize * 0.5f;
            var rect = Inflate(imageRect, -half);
            var radius = MathF.Max(0f, s.CornerRadius - half);
            using var insetPaint = new SKPaint
            {
                Color = s.EffectiveInsetColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = s.InsetSize,
                IsAntialias = true,
            };
            DrawRoundedOrRect(canvas, rect, radius, insetPaint);
        }
    }

    private static void DrawBackground(SKCanvas canvas, EditorState s, SKRect contentRect)
    {
        switch (s.Background)
        {
            case BackgroundKind.SolidColor:
                using (var fill = new SKPaint { Color = s.BackgroundColor, IsAntialias = false })
                    canvas.DrawRect(contentRect, fill);
                break;

            case BackgroundKind.Gradient:
            {
                GradientSpec? spec = null;
                if (s.BackgroundGradient is not null && GradientParser.TryParse(s.BackgroundGradient, out var parsed))
                    spec = parsed;

                if (spec is null || spec.Stops.Count < 2)
                {
                    // 畸形 CSS：退化为纯色，不至于渲染出空白
                    using var fallback = new SKPaint { Color = s.BackgroundColor, IsAntialias = false };
                    canvas.DrawRect(contentRect, fallback);
                    break;
                }

                // CSS 角度 → 方向向量：0deg=向上，顺时针。135deg = 左上→右下。
                var rad = spec.AngleDeg * MathF.PI / 180f;
                var dx = MathF.Sin(rad);
                var dy = -MathF.Cos(rad);
                var cx = contentRect.MidX;
                var cy = contentRect.MidY;
                // halfExtent 取半对角线，保证渐变覆盖整个矩形（轻微过扫描，标准做法）
                var halfExtent = MathF.Sqrt(contentRect.Width * contentRect.Width + contentRect.Height * contentRect.Height) * 0.5f;
                var start = new SKPoint(cx - dx * halfExtent, cy - dy * halfExtent);
                var end = new SKPoint(cx + dx * halfExtent, cy + dy * halfExtent);

                var colors = spec.Stops.Select(st => st.Color).ToArray();
                var pos = spec.Stops.Select(st => st.Pos / 100f).ToArray();
                using var shader = SKShader.CreateLinearGradient(start, end, colors, pos, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { Shader = shader, IsAntialias = false };
                canvas.DrawRect(contentRect, paint);
                break;
            }
        }
    }

    private static SKRect Inflate(SKRect r, float d) => new(r.Left - d, r.Top - d, r.Right + d, r.Bottom + d);

    private static void DrawRoundedOrRect(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        if (radius > 0f)
            canvas.DrawRoundRect(new SKRoundRect(rect, radius), paint);
        else
            canvas.DrawRect(rect, paint);
    }
}
