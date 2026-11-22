using Novashot.Core.Clipboard;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// B-1：剪贴板 PNG 载荷（Win32 注册格式 "PNG"）编解码语义测试。锁定：
/// - PNG 字节 → SKBitmap 解码，不透明像素逐像素相等
/// - alpha 通道经 PNG 往返不丢（透明背景标注复制的保真前提）
/// - 畸形字节返回 null 不抛（与 DibCodec.FromDib 同语义，调用方继续试下一候选格式）
/// - encode：SKBitmap → PNG 字节（写入侧 alongside DIB 的互操作载荷）
/// </summary>
public class PngCodecTests
{
    private static SKBitmap Solid(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static SKBitmap Striped(int w, int h)
    {
        var bmp = Solid(w, h, SKColors.Black);
        unsafe
        {
            var p = (uint*)bmp.GetPixels();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte r = (byte)(x * 255 / Math.Max(1, w - 1));
                    byte g = (byte)(y * 255 / Math.Max(1, h - 1));
                    byte b = (byte)((x + y) & 0xFF);
                    p[y * w + x] = 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
                }
            }
        }
        return bmp;
    }

    // ---------- encode（写入侧载荷） ----------

    [Fact]
    public void Encode_ValidBitmap_ProducesPngBytes()
    {
        using var bmp = Striped(5, 4);
        byte[]? png = PngCodec.Encode(bmp);

        Assert.NotNull(png);
        Assert.True(png!.Length > 8);
        // PNG 魔数 89 50 4E 47（放出去的载荷确实是 PNG）
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
    }

    [Fact]
    public void Encode_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PngCodec.Encode(null!));
    }

    // ---------- decode（读取侧回退） ----------

    [Fact]
    public void Decode_PngBytes_RoundtripPreservesPixels()
    {
        using var src = Striped(17, 13); // 非 4 倍宽，跨行取色防行列串位
        using var dst = PngCodec.Decode(PngCodec.Encode(src)!);

        Assert.NotNull(dst);
        Assert.Equal(src.Width, dst!.Width);
        Assert.Equal(src.Height, dst.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                Assert.Equal(src.GetPixel(x, y), dst.GetPixel(x, y));
    }

    [Fact]
    public void Decode_PngWithAlpha_PreservesTransparency()
    {
        // alpha 通道经 PNG 往返不丢（premul/unpremul 表示下 alpha 字节同值，无量化歧义）——
        // 全透明被拍平成 opaque 是本测试要堵的回归（DIB 路径按 M6c 定案就是不透明语义）
        using var src = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        src.Erase(SKColors.Transparent);
        unsafe
        {
            var p = (uint*)src.GetPixels();
            p[0] = 0x80800000u; // 内存 B G R A 直写：premul 50% 红（R=128, A=128）
        }

        using var dst = PngCodec.Decode(PngCodec.Encode(src)!);

        Assert.NotNull(dst);
        Assert.Equal(128, dst!.GetPixel(0, 0).Alpha); // 50% alpha 保持
        Assert.Equal(0, dst.GetPixel(1, 1).Alpha);    // 全透明保持（不被拍平）
    }

    [Fact]
    public void Decode_Garbage_ReturnsNull()
    {
        // 剪贴板载荷是跨应用不可信数据：畸形字节必须 null 收场，不允许抛
        Assert.Null(PngCodec.Decode(Array.Empty<byte>()));            // 空
        Assert.Null(PngCodec.Decode(new byte[] { 1, 2, 3, 4, 5, 6 })); // 非 PNG
        Assert.Null(PngCodec.Decode(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' })); // 魔数但截断
    }
}
