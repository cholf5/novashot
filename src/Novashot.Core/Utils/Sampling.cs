using SkiaSharp;

namespace Novashot.Core.Utils;

/// <summary>
/// 源图缩放重采样的统一口径（消费点：Composition.RenderScene 编辑器预览、
/// ThumbnailWriter 缩略图）。
///
/// 为什么必须显式给采样：SKCanvas.DrawBitmap 无参重载走 struct 默认 SKSamplingOptions
/// （= nearest、无 mipmap），任何非 1:1 缩放都会把高频细节嚼成摩尔纹/马赛克
/// （4K@150% 下「预览糊、导出清晰」的根因；MosaicAnnotation 的 nearest 放大属刻意像素化，另计）。
/// linear + mipmap 在任意缩小倍率下平滑；1:1 恒等变换（导出/直绘）下采样不生效，输出字节不变。
/// </summary>
public static class Sampling
{
    public static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);
}
