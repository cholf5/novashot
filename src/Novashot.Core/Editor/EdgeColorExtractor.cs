using Novashot.Core.Annotation;
using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// Auto edge color（M6d）：取（裁剪后的）源图边缘带的主色，作为合成背景纯色。
/// 语义对齐常见"智能背景"实现：四边各采 2px 带（小于 8px 的边按边长一半采样），
/// RGB 各量化到 16 级（高 4 bit）建直方图，取众数桶的桶均值；alpha 一律拉满 255。
/// </summary>
public static class EdgeColorExtractor
{
    /// <summary>边缘采样带宽度（px，单边）。</summary>
    private const int BandPx = 2;

    public static SKColor Extract(AnnotationDocument? document, EditorState? state = null)
    {
        if (document is null)
            return SKColors.White;
        var source = document.Source;
        if (source is null || source.Width <= 0 || source.Height <= 0)
            return SKColors.White;

        // 有 crop 时取裁剪后区域
        int left = 0, top = 0, width = source.Width, height = source.Height;
        if (state?.CropRect is { } cropRect)
        {
            var clamped = CropRects.ClampToImage(cropRect, new SKRect(0, 0, source.Width, source.Height));
            if (clamped is null) return SKColors.White;
            var c = clamped.Value;
            left = (int)c.Left;
            top = (int)c.Top;
            width = (int)c.Width;
            height = (int)c.Height;
        }

        var band = Math.Clamp(Math.Min(width, height) / 4, 1, BandPx);

        // 直方图 key：R/G/B 高 4 bit；value：累计 (count, sumR, sumG, sumB)
        var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();

        void Sample(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            var c = source.GetPixel(left + x, top + y);
            int key = ((c.Red >> 4) << 8) | ((c.Green >> 4) << 4) | (c.Blue >> 4);
            var (cnt, sr, sg, sb) = buckets.GetValueOrDefault(key);
            buckets[key] = (cnt + 1, sr + c.Red, sg + c.Green, sb + c.Blue);
        }

        for (int y = 0; y < height; y++)
        {
            for (int b = 0; b < band; b++)
            {
                Sample(b, y);                  // 左带
                Sample(width - 1 - b, y);      // 右带
            }
        }
        for (int x = 0; x < width; x++)
        {
            for (int b = 0; b < band; b++)
            {
                Sample(x, b);                  // 顶带
                Sample(x, height - 1 - b);     // 底带
            }
        }

        (int Count, long R, long G, long B) best = default;
        foreach (var v in buckets.Values)
            if (v.Count > best.Count) best = v;

        if (best.Count == 0) return SKColors.White;
        return new SKColor(
            (byte)(best.R / best.Count),
            (byte)(best.G / best.Count),
            (byte)(best.B / best.Count),
            255);
    }
}
