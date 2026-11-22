using SkiaSharp;

namespace Novashot.Core.Editor;

public enum BackgroundKind
{
    /// <summary>透明背景（PNG 保留 alpha；JPEG 编码时会变黑）。</summary>
    None,

    /// <summary>纯色背景。</summary>
    SolidColor,

    /// <summary>24 预设渐变之一（<see cref="BackgroundGradient"/> 为 CSS 串）。</summary>
    Gradient,
}

/// <summary>边框描边相对图像边缘的位置（对齐 WinShot borderType）。</summary>
public enum BorderStyle
{
    Outside,
    Center,
    Inside,
}

/// <summary>
/// 文档级合成状态（背景 / padding / 圆角 / 阴影 / 内衬 / 边框）。
/// 不可变 record：改一项 = <c>with</c> 新实例（与 <c>AnnotationStyle</c> 同策略）；
/// 变更不进 Undo（延续 M4c 样式决定）。默认值 = 无任何效果，
/// 此时 <see cref="Composition"/> 输出与 M6a 的裸文档渲染逐像素一致。
/// 颜色参数为可空（SKColor 无编译期常量可用）：null = 各自的默认色（白）。
/// </summary>
public sealed record class EditorState(
    BackgroundKind Background = BackgroundKind.None,
    SKColor BackgroundColor = default,
    string? BackgroundGradient = null,
    int Padding = 0,
    float CornerRadius = 0f,
    float ShadowSize = 0f,
    float InsetSize = 0f,
    SKColor? InsetColor = null,
    float BorderWidth = 0f,
    SKColor? BorderColor = null,
    BorderStyle Border = BorderStyle.Center,
    SKRect? CropRect = null)
{
    public static readonly EditorState Default = new();

    /// <summary>阴影常量：纯 blur、无位移、半透明黑（对齐 WinShot 默认观感）。</summary>
    public static readonly SKColor ShadowColor = new(0, 0, 0, 0x55);

    public static readonly SKColor DefaultFrameColor = new(0xFF, 0xFF, 0xFF);

    public SKColor EffectiveInsetColor => InsetColor ?? DefaultFrameColor;
    public SKColor EffectiveBorderColor => BorderColor ?? DefaultFrameColor;

    public bool HasBackground => Background != BackgroundKind.None;

    /// <summary>
    /// M6d 非破坏裁剪（image 像素空间，Standardized）。null = 不裁剪。
    /// 裁剪裁的是<b>显示</b>：越界标注被 clip，撤销即恢复，标注数据不动。
    /// </summary>
    public SKRect? CropRect { get; init; } = CropRect;

    /// <summary>返回钳制到安全区间的副本（UI 可能给出越界值）。crop 矩形钳回图像范围。</summary>
    public EditorState Clamped(SKSizeI imageSize) => this with
    {
        Padding = Math.Clamp(Padding, 0, 96),
        CornerRadius = Math.Clamp(CornerRadius, 0f, 64f),
        ShadowSize = Math.Clamp(ShadowSize, 0f, 64f),
        InsetSize = Math.Clamp(InsetSize, 0f, 32f),
        BorderWidth = Math.Clamp(BorderWidth, 0f, 16f),
        CropRect = CropRect is { } crop
            ? CropRects.ClampToImage(crop, new SKRect(0, 0, imageSize.Width, imageSize.Height))
            : null,
    };

    /// <summary>无图像尺寸上下文时的钳制（仅数值参数；crop 无法校验时保持原样）。</summary>
    public EditorState Clamped() => this with
    {
        Padding = Math.Clamp(Padding, 0, 96),
        CornerRadius = Math.Clamp(CornerRadius, 0f, 64f),
        ShadowSize = Math.Clamp(ShadowSize, 0f, 64f),
        InsetSize = Math.Clamp(InsetSize, 0f, 32f),
        BorderWidth = Math.Clamp(BorderWidth, 0f, 16f),
    };
}
