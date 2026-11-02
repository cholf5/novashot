using SkiaSharp;

namespace Prtsc.Core.Editor;

/// <summary>
/// 透明区域占位格（棋盘纹理）——编辑器预览专用。
///
/// 导出永不画它：它表达的正是"这里导出后是透明的"，画进导出就等于把提示当成内容。
/// 之所以需要它：<see cref="BackgroundKind.None"/> 时内容区可能是透明的
/// （padding 环、圆角外角、透明源图），而编辑器衬底本身就是暗色——
/// 两者同色，用户看不出画布边界在哪，直到导出才发现图边上多了一圈透明。
///
/// 配色是暗色 UI 里的中性灰（比衬底亮一档），既不打眼也不给截图上色偏。
/// </summary>
public static class TransparencyChecker
{
    /// <summary>格子边长（DIP，视口尺度）：不随画布缩放变化，永远是这个视觉尺寸。</summary>
    public const float DefaultCellSize = 8f;

    /// <summary>浅格。</summary>
    public static readonly SKColor LightColor = new(0x38, 0x38, 0x3F);

    /// <summary>深格。</summary>
    public static readonly SKColor DarkColor = new(0x23, 0x23, 0x29);

    /// <summary>
    /// 在 <paramref name="rect"/>（当前 canvas 空间）内铺棋盘格。图案原点对齐矩形左上角，
    /// 所以矩形换位置时格子不会"滑动"；空矩形直接跳过。
    /// </summary>
    public static void Draw(SKCanvas canvas, SKRect rect,
        float cellSize = DefaultCellSize, SKColor? light = null, SKColor? dark = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (rect.Width <= 0f || rect.Height <= 0f) return;

        // 整数格宽 = 边缘不出半像素灰线（导出边界处尤其明显）
        var cell = MathF.Max(2f, MathF.Round(cellSize));
        using var tile = CreateTile((int)cell, light ?? LightColor, dark ?? DarkColor);
        // 平铺原点钉在矩形左上角（取整避免亚像素模糊）
        var local = SKMatrix.CreateTranslation(MathF.Round(rect.Left), MathF.Round(rect.Top));
        using var shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, local);
        using var paint = new SKPaint { Shader = shader, IsAntialias = false };
        canvas.DrawRect(rect, paint);
    }

    /// <summary>2×2 格的小瓦片：左上/右下浅，右上/左下深。</summary>
    private static SKBitmap CreateTile(int cell, SKColor light, SKColor dark)
    {
        var size = cell * 2;
        var tile = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(tile);
        canvas.Clear(light);
        using var darkPaint = new SKPaint { Color = dark, IsAntialias = false };
        canvas.DrawRect(cell, 0, cell, cell, darkPaint);
        canvas.DrawRect(0, cell, cell, cell, darkPaint);
        return tile;
    }
}
