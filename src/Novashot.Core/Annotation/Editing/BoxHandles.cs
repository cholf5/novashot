using SkiaSharp;

namespace Novashot.Core.Annotation.Editing;

/// <summary>
/// 8-handle 盒子手柄辅助函数：矩形、椭圆、聚光灯共用同一套 handle 拓扑。
/// 顺序：TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left。
/// </summary>
public static class BoxHandles
{
    /// <summary>为标准化后的矩形生成 8 个手柄（坐标为像素空间）。</summary>
    public static AnnotationHandle[] GetHandles(SKRect rect)
    {
        var r = rect.Standardized;
        var midX = (r.Left + r.Right) * 0.5f;
        var midY = (r.Top + r.Bottom) * 0.5f;
        return new[]
        {
            new AnnotationHandle(HandleKind.TopLeft,     new SKPoint(r.Left,  r.Top)),
            new AnnotationHandle(HandleKind.Top,         new SKPoint(midX,    r.Top)),
            new AnnotationHandle(HandleKind.TopRight,    new SKPoint(r.Right, r.Top)),
            new AnnotationHandle(HandleKind.Right,       new SKPoint(r.Right, midY)),
            new AnnotationHandle(HandleKind.BottomRight, new SKPoint(r.Right, r.Bottom)),
            new AnnotationHandle(HandleKind.Bottom,      new SKPoint(midX,    r.Bottom)),
            new AnnotationHandle(HandleKind.BottomLeft,  new SKPoint(r.Left,  r.Bottom)),
            new AnnotationHandle(HandleKind.Left,        new SKPoint(r.Left,  midY)),
        };
    }

    /// <summary>
    /// 把指定 handle 拖到 <paramref name="newImagePos"/>，返回更新后的矩形。
    /// 允许拖过对角形成负宽/负高，调用方按需 Standardize；本方法保持"哪条边跟着手柄"的语义即可，
    /// 让 UI 上"边"移动看起来就是这条边。
    /// </summary>
    public static SKRect ApplyHandle(SKRect rect, HandleKind kind, SKPoint newImagePos)
    {
        var r = rect;
        switch (kind)
        {
            case HandleKind.TopLeft:
                r.Left = newImagePos.X;
                r.Top = newImagePos.Y;
                break;
            case HandleKind.Top:
                r.Top = newImagePos.Y;
                break;
            case HandleKind.TopRight:
                r.Right = newImagePos.X;
                r.Top = newImagePos.Y;
                break;
            case HandleKind.Right:
                r.Right = newImagePos.X;
                break;
            case HandleKind.BottomRight:
                r.Right = newImagePos.X;
                r.Bottom = newImagePos.Y;
                break;
            case HandleKind.Bottom:
                r.Bottom = newImagePos.Y;
                break;
            case HandleKind.BottomLeft:
                r.Left = newImagePos.X;
                r.Bottom = newImagePos.Y;
                break;
            case HandleKind.Left:
                r.Left = newImagePos.X;
                break;
            default:
                // 非 box 手柄不动
                break;
        }
        return r;
    }
}
