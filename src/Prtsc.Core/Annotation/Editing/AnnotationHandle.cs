using SkiaSharp;

namespace Prtsc.Core.Annotation.Editing;

/// <summary>
/// 一个可拖手柄的空间位置。坐标在源图像 <b>像素空间</b>；SkiaCanvas 用视口矩阵映射到 DIP 再绘制。
/// </summary>
public readonly record struct AnnotationHandle(HandleKind Kind, SKPoint ImagePos);
