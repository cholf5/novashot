using SkiaSharp;

namespace Novashot.Core.Clipboard;

/// <summary>
/// B-1：剪贴板 PNG 载荷（Win32 注册格式 "PNG"）↔ SKBitmap 纯字节转换。
/// Chrome/Electron 应用（右键复制图片、微信截图等）常只放注册格式 "PNG" 而无 DIB，
/// 读取路径用它兜底；写入侧用它生成 alongside DIB 的 PNG 载荷（只认 PNG 字节的读者互操作）。
/// 与 Mac（public.png）/Linux（image/png）平台层同语义：委托 Skia 编解码，畸形字节返回 null 不抛。
/// </summary>
public static class PngCodec
{
    /// <summary>PNG 字节 → SKBitmap（alpha 保留；Skia 按源 PNG 给 opaque/premul）。非 PNG/损坏字节返回 null（调用方继续试下一候选格式）。</summary>
    public static SKBitmap? Decode(ReadOnlySpan<byte> png)
    {
        // SkiaSharp 3.x 的 span 重载在无法建 SKCodec 的字节（非 PNG/严重截断）上会抛
        // ArgumentNullException('codec') 而不是返回 null——剪贴板载荷是跨应用不可信数据，
        // 按 DibCodec.FromDib 同款契约转成 null，绝不带异常出解码层
        try
        {
            return SKBitmap.Decode(png);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    /// <summary>SKBitmap → PNG 字节（quality 100 无损）。编码失败返回 null（写入侧按尽力放处理，不影响 DIB 双格式成败）。</summary>
    public static byte[]? Encode(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
