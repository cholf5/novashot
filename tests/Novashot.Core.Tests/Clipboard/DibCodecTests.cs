using Novashot.Core.Clipboard;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// DIB (CF_DIBV5 / CF_DIB) 字节编解码语义测试。锁定：
/// - 头部字段（biSize=124 / biHeight=-h / BI_BITFIELDS / masks）
/// - 像素字节序：内存 BGRA、行序 top-down
/// - roundtrip：任意不透明 SKBitmap → DIB → SKBitmap 逐像素相等
/// - 24bpp CF_DIB 旧格式可读
/// - 畸形输入返回 null（不抛）
/// </summary>
public class DibCodecTests
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

    // ---------- 写出 CF_DIBV5 ----------

    [Fact]
    public void ToDibV5_Header_Fields()
    {
        using var bmp = Solid(3, 2, SKColors.Red);
        byte[] dib = DibCodec.ToDibV5(bmp);

        // BITMAPV5HEADER: biSize(4) biWidth(4) biHeight(4) biPlanes(2) biBitCount(2)
        //                 biCompression(4) biSizeImage(4)
        Assert.Equal(124, ReadInt32(dib, 0));            // biSize = 124
        Assert.Equal(3, ReadInt32(dib, 4));              // biWidth
        Assert.Equal(-2, ReadInt32(dib, 8));             // biHeight 负 = top-down
        Assert.Equal(1, ReadInt16(dib, 12));             // biPlanes
        Assert.Equal(32, ReadInt16(dib, 14));            // biBitCount
        Assert.Equal(3, ReadInt32(dib, 16));             // BI_BITFIELDS
        Assert.Equal(3 * 2 * 4, ReadInt32(dib, 20));     // biSizeImage

        // masks 在 V5 头内部 @40：R/G/B（与内存 BGRA 字节序对应的 DWORD 值）
        Assert.Equal(0x00FF0000u, ReadUInt32(dib, 40));
        Assert.Equal(0x0000FF00u, ReadUInt32(dib, 44));
        Assert.Equal(0x000000FFu, ReadUInt32(dib, 48));

        // 总长 = BITMAPV5HEADER 124（masks 在头内）+ 像素；无额外 palette 区
        Assert.Equal(124 + 3 * 2 * 4, dib.Length);
    }

    [Fact]
    public void ToDibV5_PixelLayout_TopDown_Bgra()
    {
        // 2x2：第一行 红 蓝，第二行 绿 黑
        using var bmp = Solid(2, 2, SKColors.Black);
        SetPixel(bmp, 0, 0, 255, 0, 0);
        SetPixel(bmp, 1, 0, 0, 0, 255);
        SetPixel(bmp, 0, 1, 0, 255, 0);

        byte[] dib = DibCodec.ToDibV5(bmp);
        int px = 124; // BITMAPV5HEADER 124 字节后紧接像素（masks 在头内）

        // 第 1 像素 = 红：内存序 B G R A
        Assert.Equal(0x00, dib[px + 0]);
        Assert.Equal(0x00, dib[px + 1]);
        Assert.Equal(0xFF, dib[px + 2]);
        Assert.Equal(0xFF, dib[px + 3]);
        // 第 2 像素 = 蓝：B=0xFF
        Assert.Equal(0xFF, dib[px + 4]);
        // top-down：+8 起是第二行第 1 像素 = 绿（G=0xFF）
        Assert.Equal(0x00, dib[px + 8]);
        Assert.Equal(0xFF, dib[px + 9]);
    }

    [Fact]
    public void ToDibV5_PremulInput_OpaqueOutput()
    {
        // 50% alpha 的红：Erase(SKColor(255,0,0,128)) 后 premul 内存 = (B=0,G=0,R=128,A=128)
        // DIB 输出应为不透明纯红（unpremul 还原 R=255，A=255）
        using var bmp = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(new SKColor(255, 0, 0, 128));
        byte[] dib = DibCodec.ToDibV5(bmp);
        int px = 124;
        Assert.Equal(0xFF, dib[px + 3]);          // A=255
        Assert.Equal(0xFF, dib[px + 2]);          // R=255（unpremul 后红分量满）
    }

    // ---------- 解析 ----------

    [Fact]
    public void FromDib_Roundtrip_PreservesPixels()
    {
        using var src = Striped(17, 13); // 非 4 倍宽（32bpp 行天然对齐，仍可测行序）
        byte[] dib = DibCodec.ToDibV5(src);
        using var dst = DibCodec.FromDib(dib);
        Assert.NotNull(dst);
        Assert.Equal(src.Width, dst!.Width);
        Assert.Equal(src.Height, dst.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                Assert.Equal(src.GetPixel(x, y), dst.GetPixel(x, y));
    }

    [Fact]
    public void FromDib_CfDib24bpp_LegacyReader()
    {
        // 手工构造 24bpp BI_RGB CF_DIB（老应用只给这种）
        int w = 3, h = 2, stride = ((w * 3 + 3) / 4) * 4;
        var dib = new byte[40 + stride * h];
        WriteInt32(dib, 0, 40);      // biSize
        WriteInt32(dib, 4, w);
        WriteInt32(dib, 8, -h);      // top-down
        WriteInt16(dib, 12, 1);      // biPlanes
        WriteInt16(dib, 14, 24);     // biBitCount
        WriteInt32(dib, 16, 0);      // BI_RGB
        // 第 1 行：红 绿 蓝 —— 内存 BGR（B 在前）
        int row0 = 40;
        dib[row0 + 0] = 0x00; dib[row0 + 1] = 0x00; dib[row0 + 2] = 0xFF;
        dib[row0 + 3] = 0x00; dib[row0 + 4] = 0xFF; dib[row0 + 5] = 0x00;
        dib[row0 + 6] = 0xFF; dib[row0 + 7] = 0x00; dib[row0 + 8] = 0x00;
        // 第 2 行：白
        for (int x = 0; x < w; x++)
        {
            dib[40 + stride + x * 3 + 0] = 0xFF;
            dib[40 + stride + x * 3 + 1] = 0xFF;
            dib[40 + stride + x * 3 + 2] = 0xFF;
        }

        using var bmp = DibCodec.FromDib(dib);
        Assert.NotNull(bmp);
        Assert.Equal(w, bmp!.Width);
        Assert.Equal(h, bmp.Height);
        Assert.Equal(SKColors.Red, bmp.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bmp.GetPixel(1, 0));
        Assert.Equal(SKColors.Blue, bmp.GetPixel(2, 0));
        Assert.Equal(SKColors.White, bmp.GetPixel(1, 1));
    }

    [Fact]
    public void FromDib_Garbage_ReturnsNull()
    {
        Assert.Null(DibCodec.FromDib(new byte[10]));            // 太短
        Assert.Null(DibCodec.FromDib(Array.Empty<byte>()));     // 空
        var bad = new byte[64];
        WriteInt32(bad, 0, 40);
        WriteInt32(bad, 8, 5);      // 正 biHeight（bottom-up）——本轮不支持
        WriteInt16(bad, 14, 32);
        Assert.Null(DibCodec.FromDib(bad));
    }

    [Fact]
    public void ToDibV5_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DibCodec.ToDibV5(null!));
    }

    // ---------- helpers ----------

    private static void SetPixel(SKBitmap bmp, int x, int y, byte r, byte g, byte b)
        => bmp.SetPixel(x, y, new SKColor(r, g, b));

    private static int ReadInt32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static uint ReadUInt32(byte[] b, int o) => (uint)ReadInt32(b, o);
    private static int ReadInt16(byte[] b, int o) => (short)(b[o] | (b[o + 1] << 8));
    private static void WriteInt32(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }
    private static void WriteInt16(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
    }
}
