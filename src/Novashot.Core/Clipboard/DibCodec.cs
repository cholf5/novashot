using SkiaSharp;

namespace Novashot.Core.Clipboard;

/// <summary>
/// SKBitmap ↔ Windows DIB（CF_DIBV5 / CF_DIB 载荷）纯字节转换。
/// 只处理 32bpp（BGRA，top-down）与 24bpp（BGR，top-down）两种读入格式；
/// 写出固定 CF_DIBV5（BI_BITFIELDS + 透明调色板区，兼容 WinShot 同款布局）。
/// Premul 输入先 unpremul 到 opaque（A=255）——剪贴板位图按不透明语义传递。
/// </summary>
public static class DibCodec
{
    private const int V5HeaderSize = 124;
    private const int InfoHeaderSize = 40;
    private const int MasksSize = 12;   // R/G/B 三个 DWORD
    private const int PaletteArea = 16; // V5 头后的透明色板区（4 DWORD 全 0）

    private const int BiRgb = 0;
    private const int BiBitfields = 3;
    private const int LcsSRgb = unchecked((int)0x7352_4742); // 'sRGB'

    // ---------- 写：SKBitmap → CF_DIBV5 字节 ----------

    /// <summary>编码为 CF_DIBV5 载荷（BITMAPV5HEADER（masks 在头内）+ top-down BGRA 像素）。</summary>
    public static byte[] ToDibV5(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        int w = bitmap.Width, h = bitmap.Height;
        int pixelBytes = w * h * 4;
        var dib = new byte[V5HeaderSize + pixelBytes];

        WriteV5Header(dib, w, h);
        CopyPixelsOpaque(bitmap, dib, V5HeaderSize);
        return dib;
    }

    /// <summary>编码为传统 CF_DIB 载荷（BITMAPINFOHEADER + top-down BGRX 像素）。</summary>
    public static byte[] ToDib(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        int w = bitmap.Width, h = bitmap.Height;
        int pixelBytes = w * h * 4;
        var dib = new byte[InfoHeaderSize + pixelBytes];

        WriteInt32(dib, 0, InfoHeaderSize);
        WriteInt32(dib, 4, w);
        WriteInt32(dib, 8, -h);            // top-down
        WriteInt16(dib, 12, 1);            // biPlanes
        WriteInt16(dib, 14, 32);           // biBitCount
        WriteInt32(dib, 16, BiRgb);
        WriteInt32(dib, 20, pixelBytes);

        CopyPixelsOpaque(bitmap, dib, InfoHeaderSize);
        return dib;
    }

    private static void WriteV5Header(byte[] dib, int w, int h)
    {
        WriteInt32(dib, 0, V5HeaderSize);
        WriteInt32(dib, 4, w);
        WriteInt32(dib, 8, -h);            // 负 biHeight = top-down
        WriteInt16(dib, 12, 1);            // biPlanes
        WriteInt16(dib, 14, 32);           // biBitCount
        WriteInt32(dib, 16, BiBitfields);
        WriteInt32(dib, 20, w * h * 4);    // biSizeImage
        // biXPelsPerMeter @24, biYPelsPerMeter @28, biClrUsed @32, biClrImportant @36: 0
        // V5 masks 在头内部：R@40 G@44 B@48 A@52（与内存 BGRA 字节序对应的 DWORD 值）
        WriteInt32(dib, 40, unchecked((int)0x00FF0000)); // R
        WriteInt32(dib, 44, unchecked((int)0x0000FF00)); // G
        WriteInt32(dib, 48, 0x000000FF);                 // B
        WriteInt32(dib, 52, unchecked((int)0xFF000000)); // A（输出恒为 opaque）
        WriteInt32(dib, 56, LcsSRgb);      // bV5CSType：sRGB（0 = calibrated + 全 0 gamma，部分读者会色偏）
        // bV5Endpoints @60..95 / gamma @96..107 / intent @108 / profile @112..123: 0
    }

    private static unsafe void CopyPixelsOpaque(SKBitmap bitmap, byte[] dib, int offset)
    {
        int w = bitmap.Width, h = bitmap.Height;
        byte* src = (byte*)bitmap.GetPixels();
        if (src is null)
            throw new ArgumentException("Bitmap has no pixel buffer.", nameof(bitmap));
        fixed (byte* dst = dib)
        {
            byte* d = dst + offset;
            bool premul = bitmap.Info.AlphaType == SKAlphaType.Premul;
            for (int i = 0; i < w * h; i++)
            {
                byte b = src[i * 4 + 0], g = src[i * 4 + 1], r = src[i * 4 + 2], a = src[i * 4 + 3];
                if (premul && a > 0 && a < 255)
                {
                    // unpremul 后按不透明输出（Math.Round 同 Skia unpremul 行为）
                    r = (byte)Math.Min(255, (r * 255 + a / 2) / a);
                    g = (byte)Math.Min(255, (g * 255 + a / 2) / a);
                    b = (byte)Math.Min(255, (b * 255 + a / 2) / a);
                }
                d[i * 4 + 0] = b;
                d[i * 4 + 1] = g;
                d[i * 4 + 2] = r;
                d[i * 4 + 3] = 0xFF;
            }
        }
    }

    // ---------- 读：DIB 字节 → SKBitmap ----------

    /// <summary>
    /// 解析 CF_DIBV5 / CF_DIB 载荷。支持 32bpp（BGRA）与 24bpp（BGR）BI_BITFIELDS/BI_RGB，
    /// 仅 top-down（负 biHeight）。宽度不含行对齐 pad 的 24bpp 也正确处理。
    /// 畸形输入返回 null。
    /// </summary>
    public static SKBitmap? FromDib(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < InfoHeaderSize) return null;

        int headerSize = ReadInt32(dib, 0);
        int w = ReadInt32(dib, 4);
        int height = ReadInt32(dib, 8);
        int bitCount = ReadInt16(dib, 14);
        int compression = ReadInt32(dib, 16);

        if (headerSize < InfoHeaderSize || w <= 0 || height >= 0 || bitCount < 24)
            return null;
        int h = -height;

        long stride = (((long)w * bitCount + 31) / 32) * 4; // 4 字节对齐行距（按 bit 数算，防止溢出）
        if ((long)h * stride > dib.Length - headerSize) return null;

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (!CopyPixelsFromDib(dib, headerSize, bmp, w, h, bitCount))
        {
            bmp.Dispose();
            return null;
        }
        return bmp;
    }

    private static unsafe bool CopyPixelsFromDib(ReadOnlySpan<byte> dib, int pixelOffset, SKBitmap bmp, int w, int h, int bitCount)
    {
        if (bitCount != 32 && bitCount != 24) return false;
        long stride = (((long)w * bitCount + 31) / 32) * 4;

        fixed (byte* src = dib)
        {
            byte* s = src + pixelOffset;
            byte* dst = (byte*)bmp.GetPixels();
            if (dst is null) return false;
            for (int y = 0; y < h; y++)
            {
                byte* row = s + (long)y * stride;
                byte* outRow = dst + (long)y * w * 4;
                if (bitCount == 32)
                {
                    for (int x = 0; x < w; x++)
                    {
                        outRow[x * 4 + 0] = row[x * 4 + 0];
                        outRow[x * 4 + 1] = row[x * 4 + 1];
                        outRow[x * 4 + 2] = row[x * 4 + 2];
                        outRow[x * 4 + 3] = 0xFF;
                    }
                }
                else // 24bpp BGR
                {
                    for (int x = 0; x < w; x++)
                    {
                        outRow[x * 4 + 0] = row[x * 3 + 0];
                        outRow[x * 4 + 1] = row[x * 3 + 1];
                        outRow[x * 4 + 2] = row[x * 3 + 2];
                        outRow[x * 4 + 3] = 0xFF;
                    }
                }
            }
        }
        return true;
    }

    // ---------- little-endian helpers ----------

    private static int ReadInt32(ReadOnlySpan<byte> b, int o) =>
        b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

    private static int ReadInt16(ReadOnlySpan<byte> b, int o) => (short)(b[o] | (b[o + 1] << 8));

    private static void WriteInt32(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }

    private static void WriteInt16(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
    }
}
