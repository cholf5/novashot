using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// B-4：crop 框 8 手柄 reshape 纯逻辑。crop 激活时（CropRect 已应用 + X 工具选中）
/// 调用方（SkiaCanvas）按下用 <see cref="HitTest"/> 判定是否命中手柄，命中后拖拽每步用
/// <see cref="DragTo"/> 换算新框并经 EditorViewModel.ApplyCrop 实时写回；
/// Esc / CaptureLost 由调用方恢复拖拽起始框。全部坐标 image 像素空间、Standardized 语义。
/// </summary>
public static class CropReshape
{
    /// <summary>reshape 的最小宽/高（image px）：手柄不许拖过对边，兜住误触归零。
    /// 与 CropTool.MinAreaPx（新框提交阈值）同值但语义不同：一边调参需同步评估另一边。</summary>
    public const float MinPx = 4f;

    /// <summary>
    /// 手柄命中测试：BoxHandles 8 手柄拓扑上的距离命中（slop 为 image 空间半径，
    /// 由 DIP 半径经视口 scale 换算，同 SelectTool 口径）。
    /// </summary>
    public static HandleKind? HitTest(SKRect cropRect, SKPoint imagePos, float slop)
    {
        var rSq = slop * slop;
        foreach (var h in BoxHandles.GetHandles(cropRect))
        {
            var dx = imagePos.X - h.ImagePos.X;
            var dy = imagePos.Y - h.ImagePos.Y;
            if (dx * dx + dy * dy <= rSq) return h.Kind;
        }
        return null;
    }

    /// <summary>
    /// 把 <paramref name="kind"/> 手柄拖到 <paramref name="handlePos"/>，返回新裁剪框：
    /// ① 手柄钳制（图像内 + 不越过对边，保持 ≥ <see cref="MinPx"/>）→ ② ApplyHandle →
    /// ③ 比例吸附（ratioW/H &gt; 0 时，锚定对边/对角）→ ④ 兜底钳回图像。
    /// </summary>
    public static SKRect DragTo(SKRect cropRect, HandleKind kind, SKPoint handlePos, SKRect imageBounds,
        float ratioW, float ratioH)
    {
        var r = cropRect.Standardized;

        // ① 手柄位置钳制：先图像内，再不许越过对边（对边固定 → 最小尺寸保证）。
        // 应用态框可能已被 SnapToRatio/细条拖拽压到 MinPx 以下（如 3:4 吸附 200×5 细条 → 宽 3.75），
        // 此时对边钳制区间会反转——一律走 ClampOrdered 收敛到边界，不抛异常（review C1）。
        var pos = new SKPoint(
            Math.Clamp(handlePos.X, imageBounds.Left, imageBounds.Right),
            Math.Clamp(handlePos.Y, imageBounds.Top, imageBounds.Bottom));
        pos = ClampAgainstOppositeEdges(r, kind, pos, imageBounds);

        // ② 移动被拖的边（钳制后必不交叉，标准化为守卫）
        var raw = BoxHandles.ApplyHandle(r, kind, pos).Standardized;

        // ③ 比例吸附：锚定对边/对角
        if (ratioW > 0 && ratioH > 0)
            raw = FitToRatio(raw, kind, ratioW / ratioH, imageBounds);

        // ④ 兜底：MinPx 下限（极端几何下比例让位）+ 钳回图像
        raw = new SKRect(
            raw.Left, raw.Top,
            MathF.Max(raw.Right, raw.Left + MinPx),
            MathF.Max(raw.Bottom, raw.Top + MinPx));
        return CropRects.ClampToImage(raw, imageBounds) ?? r;
    }

    /// <summary>手柄不许拖过对边：沿被拖轴钳到 [对边 ± MinPx, 图像界] 区间。
    /// 区间因应用态框低于 MinPx 而反转时收敛到下界（退化框兜底，review C1）。</summary>
    private static SKPoint ClampAgainstOppositeEdges(SKRect r, HandleKind kind, SKPoint pos, SKRect img)
    {
        float x = pos.X, y = pos.Y;
        switch (kind)
        {
            case HandleKind.TopLeft:
                x = ClampOrdered(x, img.Left, r.Right - MinPx);
                y = ClampOrdered(y, img.Top, r.Bottom - MinPx);
                break;
            case HandleKind.Top:
                y = ClampOrdered(y, img.Top, r.Bottom - MinPx);
                break;
            case HandleKind.TopRight:
                x = ClampOrdered(x, r.Left + MinPx, img.Right);
                y = ClampOrdered(y, img.Top, r.Bottom - MinPx);
                break;
            case HandleKind.Right:
                x = ClampOrdered(x, r.Left + MinPx, img.Right);
                break;
            case HandleKind.BottomRight:
                x = ClampOrdered(x, r.Left + MinPx, img.Right);
                y = ClampOrdered(y, r.Top + MinPx, img.Bottom);
                break;
            case HandleKind.Bottom:
                y = ClampOrdered(y, r.Top + MinPx, img.Bottom);
                break;
            case HandleKind.BottomLeft:
                x = ClampOrdered(x, img.Left, r.Right - MinPx);
                y = ClampOrdered(y, r.Top + MinPx, img.Bottom);
                break;
            case HandleKind.Left:
                x = ClampOrdered(x, img.Left, r.Right - MinPx);
                break;
        }
        return new SKPoint(x, y);
    }

    /// <summary>有序安全钳制：hi &lt; lo 时收敛到 lo（退化框兜底，review C1）。</summary>
    private static float ClampOrdered(float v, float lo, float hi) => Math.Clamp(v, lo, MathF.Max(lo, hi));

    /// <summary>
    /// 比例吸附，锚定被拖手柄的对边/对角：角手柄 fit 进拖拽点隐含盒内（必在图像内）；
    /// 边手柄保持自由维 span，另一维 = span/ratio 锚定对边；越界时整体收窄保比例（锚边固定）。
    /// </summary>
    private static SKRect FitToRatio(SKRect r, HandleKind kind, float ratio, SKRect img)
    {
        switch (kind)
        {
            case HandleKind.TopLeft:
            {
                var (w, h) = FitSize(r.Width, r.Height, ratio);
                return new SKRect(r.Right - w, r.Bottom - h, r.Right, r.Bottom);
            }
            case HandleKind.TopRight:
            {
                var (w, h) = FitSize(r.Width, r.Height, ratio);
                return new SKRect(r.Left, r.Bottom - h, r.Left + w, r.Bottom);
            }
            case HandleKind.BottomRight:
            {
                var (w, h) = FitSize(r.Width, r.Height, ratio);
                return new SKRect(r.Left, r.Top, r.Left + w, r.Top + h);
            }
            case HandleKind.BottomLeft:
            {
                var (w, h) = FitSize(r.Width, r.Height, ratio);
                return new SKRect(r.Right - w, r.Top, r.Right, r.Top + h);
            }
            case HandleKind.Top:
            {
                // 宽固定：高 = 宽/ratio，锚下边；可用高不足 → 收窄保比例（水平居中、锚下边）。
                // 两级 fallback：若收窄后的宽度仍越出图像（h2*ratio &gt; 图像宽），
                // 交由 DragTo 步骤④的 ClampToImage 兔底——比例让位于图像钳制（review M3 口径）。
                var h = r.Width / ratio;
                if (r.Bottom - h >= img.Top)
                    return new SKRect(r.Left, r.Bottom - h, r.Right, r.Bottom);
                var h2 = r.Bottom - img.Top;
                var w2 = h2 * ratio;
                var cx = (r.Left + r.Right) * 0.5f;
                return new SKRect(cx - w2 * 0.5f, img.Top, cx + w2 * 0.5f, r.Bottom);
            }
            case HandleKind.Bottom:
            {
                var h = r.Width / ratio;
                if (r.Top + h <= img.Bottom)
                    return new SKRect(r.Left, r.Top, r.Right, r.Top + h);
                var h2 = img.Bottom - r.Top;
                var w2 = h2 * ratio;
                var cx = (r.Left + r.Right) * 0.5f;
                return new SKRect(cx - w2 * 0.5f, r.Top, cx + w2 * 0.5f, img.Bottom);
            }
            case HandleKind.Left:
            {
                // 高固定：宽 = 高*ratio，锚右边；可用宽不足 → 收高保比例（垂直居中、锚右边）
                var w = r.Height * ratio;
                if (r.Right - w >= img.Left)
                    return new SKRect(r.Right - w, r.Top, r.Right, r.Bottom);
                var w2 = r.Right - img.Left;
                var h2 = w2 / ratio;
                var cy = (r.Top + r.Bottom) * 0.5f;
                return new SKRect(img.Left, cy - h2 * 0.5f, r.Right, cy + h2 * 0.5f);
            }
            case HandleKind.Right:
            {
                var w = r.Height * ratio;
                if (r.Left + w <= img.Right)
                    return new SKRect(r.Left, r.Top, r.Left + w, r.Bottom);
                var w2 = img.Right - r.Left;
                var h2 = w2 / ratio;
                var cy = (r.Top + r.Bottom) * 0.5f;
                return new SKRect(r.Left, cy - h2 * 0.5f, img.Right, cy + h2 * 0.5f);
            }
            default:
                return r;
        }
    }

    /// <summary>把 (w,h) fit 到 W:H 比例：沿"相对目标比例最长"的一维内缩（同 SnapToRatio 口径）。</summary>
    private static (float W, float H) FitSize(float w, float h, float ratio)
    {
        if (w / h > ratio) return (h * ratio, h); // 太宽 → 缩宽
        return (w, w / ratio);                    // 太高 → 缩高
    }
}
