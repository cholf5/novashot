using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 单次拖拽的有状态工具。使用方每次 pointer down 都<b>新建一个实例</b>；
/// 实例内部记录拖拽起点 / 中间状态，pointer up 时决定是否产出提交项。
///
/// 输入坐标一律是<b>源图像素空间</b>（SkiaCanvas 已完成 DIP→image 反变换）。
///
/// M4a：只有 <see cref="RectangleTool"/>。M4b 会加 Ellipse / Arrow / Line / ... 各自的实现。
/// </summary>
public interface IAnnotationTool
{
    /// <summary>拖拽起始。工具接管此次交互；<paramref name="style"/> 由 ViewModel 在当次交互中冻结。</summary>
    void OnPointerDown(SKPoint imagePoint, AnnotationStyle style);

    /// <summary>拖拽移动。工具应更新内部 <see cref="Preview"/>。</summary>
    void OnPointerMove(SKPoint imagePoint);

    /// <summary>
    /// 拖拽结束。返回要提交给文档的标注；若为退化拖拽（面积过小等）返回 <c>null</c>。
    /// 无论返回什么，工具此后都视为已结束。
    /// </summary>
    AnnotationBase? OnPointerUp(SKPoint imagePoint);

    /// <summary>拖拽中的预览。SkiaCanvas 每帧渲染时叠加显示。</summary>
    AnnotationBase? Preview { get; }

    /// <summary>取消当前拖拽（如指针被强制释放）。</summary>
    void Cancel();
}
