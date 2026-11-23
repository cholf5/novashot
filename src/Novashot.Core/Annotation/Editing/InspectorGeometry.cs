using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Annotation.Editing;

/// <summary>
/// Inspector 几何数值编辑的纯映射：把 X/Y/宽/高 输入翻译成既有 Move/Resize 命令的参数。
///
/// 权限矩阵：X/Y 所有类型可编辑（<see cref="AnnotationBase.MoveBy"/> 全类型通用）；
/// 宽/高仅盒状族（8 向盒子手柄语义）。Text/Number 只有 Center 手柄、开放形状
/// （Line/Arrow/Pencil/Highlighter）是端点/路径语义——宽高一律只读。
/// </summary>
public static class InspectorGeometry
{
    /// <summary>宽/高编辑下限（像素）。</summary>
    public const float MinSize = 4f;

    /// <summary>该标注是否支持宽/高数值编辑（需要 8 向盒子手柄）。</summary>
    public static bool SupportsResize(AnnotationBase a) => a is
        RectangleAnnotation or EllipseAnnotation or SpotlightAnnotation or PixelEffectAnnotation;

    /// <summary>
    /// X/Y 输入 → <see cref="History.MoveAnnotationCommand"/> 的 delta（bbox 左上角移到 clamp 后目标点）。
    /// 目标点 clamp：bbox 完整落在 [0, imageW/H] 内（bbox 比画布大时贴原点）。
    /// </summary>
    public static SKPoint MoveDelta(SKRect bbox, float newX, float newY, int imageW, int imageH)
    {
        var maxLeft = Math.Max(0f, imageW - bbox.Width);
        var maxTop = Math.Max(0f, imageH - bbox.Height);
        var left = Math.Clamp(newX, 0f, maxLeft);
        var top = Math.Clamp(newY, 0f, maxTop);
        return new SKPoint(left - bbox.Left, top - bbox.Top);
    }

    /// <summary>
    /// 宽/高输入 → <see cref="History.ResizeAnnotationCommand"/> 的 <see cref="HandleKind.BottomRight"/>
    /// 目标点（clamp：下限 <see cref="MinSize"/>，右/下边不出画布）。
    /// </summary>
    public static SKPoint ResizeTarget(SKRect bbox, float newW, float newH, int imageW, int imageH)
    {
        var w = Math.Clamp(newW, MinSize, Math.Max(MinSize, imageW - bbox.Left));
        var h = Math.Clamp(newH, MinSize, Math.Max(MinSize, imageH - bbox.Top));
        return new SKPoint(bbox.Left + w, bbox.Top + h);
    }
}
