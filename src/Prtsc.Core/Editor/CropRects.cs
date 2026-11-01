using SkiaSharp;

namespace Prtsc.Core.Editor;

/// <summary>
/// Crop 矩形纯几何（M6d）：比例吸附与图像闭包。所有矩形为 image 像素空间、Standardized 语义。
/// </summary>
public static class CropRects
{
    /// <summary>
    /// 把 <paramref name="rect"/> 吸附到 w:h 比例：TopLeft 锚定，沿"相对目标比例最长"的一维内缩；
    /// 若越出 <paramref name="imageBounds"/> 再整体内缩到能放下（保持比例，TopLeft 微调）。
    /// w/h 非正时返回钳制后的原矩形（auto 模式）。
    /// </summary>
    public static SKRect SnapToRatio(SKRect rect, float w, float h, SKRect imageBounds)
    {
        var r = ClampToImage(rect, imageBounds) ?? rect.Standardized;
        if (w <= 0 || h <= 0 || r.Width <= 0 || r.Height <= 0)
            return r;

        float target = w / h;
        float current = r.Width / r.Height;

        if (current > target)
        {
            // 太宽：缩宽
            float newW = r.Height * target;
            r = new SKRect(r.Left, r.Top, r.Left + newW, r.Bottom);
        }
        else if (current < target)
        {
            // 太高：缩高
            float newH = r.Width / target;
            r = new SKRect(r.Left, r.Top, r.Right, r.Top + newH);
        }

        // 越界则沿对角整体缩小（保持比例）到能放进图像，TopLeft 保持
        float overflowX = MathF.Max(r.Right - imageBounds.Right, imageBounds.Left - r.Left);
        float overflowY = MathF.Max(r.Bottom - imageBounds.Bottom, imageBounds.Top - r.Top);
        if (overflowX > 0 || overflowY > 0)
        {
            float overflow = MathF.Max(overflowX, overflowY);
            float scale = MathF.Max(0.01f, 1f - overflow / MathF.Max(r.Width, r.Height));
            r = new SKRect(r.Left, r.Top, r.Left + r.Width * scale, r.Top + r.Height * scale);
            // 比例缩放后可能仍差一点，做一次钳制兜底
            r = ClampToImage(r, imageBounds) ?? r;
        }

        return r;
    }

    /// <summary>与图像求交；完全在图像外返回 null。</summary>
    public static SKRect? ClampToImage(SKRect rect, SKRect imageBounds)
    {
        var r = rect.Standardized;
        var left = MathF.Max(r.Left, imageBounds.Left);
        var top = MathF.Max(r.Top, imageBounds.Top);
        var right = MathF.Min(r.Right, imageBounds.Right);
        var bottom = MathF.Min(r.Bottom, imageBounds.Bottom);
        if (right <= left || bottom <= top) return null;
        return new SKRect(left, top, right, bottom);
    }
}
