using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// 编辑器视口：fit-to-window 基础变换 + 用户缩放/平移（纯数学，供纯单测）。
///
/// 坐标约定：视口尺寸（vw/vh）与 <see cref="Pan"/> 同单位（App 层传 DIP）；content 为内容
/// 空间尺寸（image 像素，含 padding/crop 偏移）。「绝对缩放」= 视口单位 / 内容像素，
/// App 层把「100% 物理像素」换算成 DIP/像素（= RenderScaling）后传 <see cref="ZoomToAbsolute"/>。
///
/// 缩放范围 [1, <see cref="MaxZoom"/>]：1 = fit，不提供比 fit 更小的缩放（画布从不小于一屏）。
/// clamp 不变式：zoom = 1 时强制居中（Pan 归零）；内容能盖住视口时 Pan 夹在「内容边缘不越过
/// 视口对边」，盖不住时锁定居中。
/// </summary>
public sealed class EditorViewport
{
    /// <summary>相对 fit 的最大倍率。与最小范围决策绑定（视口缩放 fit~8x），调参需同步评估滚轮步长手感。</summary>
    public const double MaxZoom = 8.0;

    /// <summary>相对 fit 的最小倍率：fit 本身，不提供更小。</summary>
    public const double MinZoom = 1.0;

    /// <summary>相对 fit 的缩放倍率；1 = fit。</summary>
    public double Zoom { get; private set; } = MinZoom;

    /// <summary>用户平移（视口单位，fit 后 DIP 空间）。</summary>
    public SKPoint Pan { get; private set; }

    /// <summary>复位到 fit（zoom = 1、pan 归零）。</summary>
    public void Reset()
    {
        Zoom = MinZoom;
        Pan = default;
    }

    /// <summary>
    /// 以视口锚点为中心缩放 factor 倍（&gt;1 放大）：无 clamp 时锚点下的内容点保持不动。
    /// factor 连乘自带 Math.Pow 语义（App 层滚轮传 1.25^delta）。
    /// </summary>
    public void ZoomAt(SKPoint anchor, double factor, double vw, double vh, SKSize content)
    {
        var next = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var eff = next / Zoom;
        // fit 空间锚点 p = (anchor - pan) / zoom；要求 next * p + pan' = anchor
        Pan = new SKPoint(
            (float)(anchor.X - eff * (anchor.X - Pan.X)),
            (float)(anchor.Y - eff * (anchor.Y - Pan.Y)));
        Zoom = next;
        Clamp(vw, vh, content);
    }

    /// <summary>以视口中心为锚，把绝对缩放设为 targetDipPerPx（越界走 clamp，低于 fit 即回到 fit）。</summary>
    public void ZoomToAbsolute(double targetDipPerPx, double vw, double vh, SKSize content)
    {
        var fitScale = ComputeFitScale(vw, vh, content);
        if (fitScale <= 1e-6) return;
        ZoomAt(new SKPoint((float)(vw / 2), (float)(vh / 2)),
            targetDipPerPx / (fitScale * Zoom), vw, vh, content);
    }

    /// <summary>按增量平移（拖拽平移用），随后 clamp。</summary>
    public void PanBy(SKPoint delta, double vw, double vh, SKSize content)
    {
        Pan = new SKPoint(Pan.X + delta.X, Pan.Y + delta.Y);
        Clamp(vw, vh, content);
    }

    /// <summary>视口/内容尺寸变化（窗口 resize、padding/crop 改动）后重夹。</summary>
    public void Clamp(double vw, double vh, SKSize content)
    {
        var fit = ComputeFitMatrix(vw, vh, content);
        var r = fit.MapRect(new SKRect(0, 0, content.Width, content.Height));
        var left = r.Left * Zoom;
        var top = r.Top * Zoom;
        var right = r.Right * Zoom;
        var bottom = r.Bottom * Zoom;

        double px, py;
        if (right - left >= vw) px = Math.Clamp(Pan.X, vw - right, -left);
        else px = (vw - (left + right)) / 2;
        if (bottom - top >= vh) py = Math.Clamp(Pan.Y, vh - bottom, -top);
        else py = (vh - (top + bottom)) / 2;
        Pan = new SKPoint((float)px, (float)py);
    }

    /// <summary>完整 内容空间 → 视口DIP 矩阵 = 用户变换（T(pan)·S(zoom)）· fit。</summary>
    public SKMatrix ComputeMatrix(double vw, double vh, SKSize content)
    {
        var m = ComputeFitMatrix(vw, vh, content);
        if (Zoom == MinZoom && Pan.X == 0 && Pan.Y == 0) return m;
        var u = SKMatrix.CreateScale((float)Zoom, (float)Zoom);
        u = u.PostConcat(SKMatrix.CreateTranslation(Pan.X, Pan.Y));
        return m.PostConcat(u);
    }

    /// <summary>fit 基础矩阵：uniform min-scale + 居中（SkiaCanvas 原口径平移到 Core）。</summary>
    public static SKMatrix ComputeFitMatrix(double vw, double vh, SKSize content)
    {
        var scale = ComputeFitScale(vw, vh, content);
        if (scale <= 0) return SKMatrix.CreateIdentity();
        var dx = (vw - content.Width * scale) / 2;
        var dy = (vh - content.Height * scale) / 2;
        var m = SKMatrix.CreateScale((float)scale, (float)scale);
        return m.PostConcat(SKMatrix.CreateTranslation((float)dx, (float)dy));
    }

    private static double ComputeFitScale(double vw, double vh, SKSize content)
        => vw <= 0 || vh <= 0 || content.Width <= 0 || content.Height <= 0
            ? 0
            : Math.Min(vw / content.Width, vh / content.Height);
}
