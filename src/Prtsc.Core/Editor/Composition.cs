using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.Core.Editor;

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

        canvas.DrawBitmap(document.Source, 0, 0);
        foreach (var annotation in document.Annotations)
        {
            if (skipAnnotation is not null && ReferenceEquals(annotation, skipAnnotation)) continue;
            annotation.Render(canvas);
        }
        preview?.Render(canvas);
    }
}
