using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// E-1 M9 屏幕取色器：物理像素位图上的取色纯逻辑。
///
/// 采样语义（batch 简报裁决）：overlay 手里的 <c>CapturedImage.Bitmap</c> 就是本屏物理像素
/// 位图，按 image 空间坐标直接采样，<b>不做任何 DPI 换算</b>——位图即物理像素，
/// 125%/150% 缩放屏天然取值正确。光标 DIP → image 坐标由调用方按 OverlayWindow 既有
/// 路径（round(dip × ScaleFactor)）换算后传入。
///
/// 所有坐标一律钳制进位图界内：放大镜贴边时越界半边重复边缘像素，不崩、不出空窗。
/// 像素访问路径与 <see cref="EdgeColorExtractor"/> 一致（SKBitmap.GetPixel）。
/// </summary>
public static class ColorSampler
{
    /// <summary>放大镜邻域半径：5 → 11×11 邻域（flameshot 同款规格）。</summary>
    public const int NeighborhoodRadius = 5;

    /// <summary>
    /// 取单点像素。(x, y) 为 image 空间（物理像素）坐标，越界钳制回界内。
    /// 位图为 null 时返回透明黑（调用方以 null 位图 = 不渲染放大镜）。
    /// </summary>
    public static SKColor Sample(SKBitmap? bitmap, int x, int y)
    {
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0) return default;
        var cx = Math.Clamp(x, 0, bitmap.Width - 1);
        var cy = Math.Clamp(y, 0, bitmap.Height - 1);
        return bitmap.GetPixel(cx, cy);
    }

    /// <summary>
    /// 取以 (cx, cy) 为中心的 (2r+1)² 邻域，行优先（左上 → 右下）。
    /// 越界坐标钳制到最近的有效像素（贴边时边缘像素重复出现）。
    /// 位图为 null 时返回空数组（放大镜渲染层据此跳过）。
    /// </summary>
    public static SKColor[] SampleNeighborhood(SKBitmap? bitmap, int cx, int cy, int radius)
    {
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0 || radius < 0)
            return Array.Empty<SKColor>();

        var size = 2 * radius + 1;
        var result = new SKColor[size * size];
        for (int row = 0; row < size; row++)
        {
            var y = Math.Clamp(cy + row - radius, 0, bitmap.Height - 1);
            for (int col = 0; col < size; col++)
            {
                var x = Math.Clamp(cx + col - radius, 0, bitmap.Width - 1);
                result[row * size + col] = bitmap.GetPixel(x, y);
            }
        }
        return result;
    }
}
