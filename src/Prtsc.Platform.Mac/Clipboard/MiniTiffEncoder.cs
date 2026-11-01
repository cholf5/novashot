using System.Buffers.Binary;
using SkiaSharp;

namespace Prtsc.Platform.Mac.Clipboard;

/// <summary>
/// 托管 Baseline TIFF 编码器（无压缩 RGBA/RGB）。
/// 用途：剪贴板 TIFF 格式的生成。为什么不用 NSBitmapImageRep.TIFFRepresentation——
/// 它把 rep/关联容器注册进 autorelease pool，且与手动引用计数的所有权交错极易过释放
/// （2026-11-01 SIGSEGV 实证，retain/release 各种平衡手法均不稳）；托管编码零 objc 对象，
/// 生命周期完全可控。产出文件可被 sips/预览/微信/Chrome 正常消费。
/// </summary>
public static class MiniTiffEncoder
{
    // TIFF 类型宽度
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    /// <summary>BGRA8888-premul 位图 → Baseline TIFF (RGBA, 无压缩) 字节。
    /// 不透明位图（alpha 全 255）输出为带 ExtraSamples(associated alpha) 的 RGBA——消费端按不透明处理。</summary>
    public static byte[] Encode(SKBitmap bitmap)
    {
        var width = bitmap.Info.Width;
        var height = bitmap.Info.Height;
        var spp = 4; // R G B A
        var pixelBytes = width * height * spp;

        // 布局：[8B header][IFD][外部数据区：BitsPerSample(8B) + XRes(8B) + YRes(8B) + 像素数据]
        const int ifdOffset = 8;
        const int entryCount = 13;
        var ifdSize = 2 + entryCount * 12 + 4;
        var extraOffset = ifdOffset + ifdSize;
        var bpsOffset = extraOffset;              // 4×SHORT = 8B
        var xResOffset = bpsOffset + 8;           // RATIONAL = 8B
        var yResOffset = xResOffset + 8;          // RATIONAL = 8B
        var pixelOffset = yResOffset + 8;

        var total = pixelOffset + pixelBytes;
        var buf = new byte[total];

        // Header: little-endian "II*\0", first IFD at 8
        buf[0] = (byte)'I'; buf[1] = (byte)'I';
        buf[2] = 0x2A; buf[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), ifdOffset);

        // IFD entries（按 tag 升序，TIFF 规范要求）；先写 entry count
        var p = ifdOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(p), (ushort)entryCount);
        p += 2;

        void Entry(ushort tag, ushort type, uint count, uint valueOrOffset)
        {
            Span<byte> e = stackalloc byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(e, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(e.Slice(2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(8), valueOrOffset);
            e.CopyTo(buf.AsSpan(p));
            p += 12;
        }

        Entry(256, TypeLong, 1, (uint)width);            // ImageWidth
        Entry(257, TypeLong, 1, (uint)height);           // ImageLength
        Entry(258, TypeShort, (uint)spp, (uint)bpsOffset);     // BitsPerSample = 8,8,8,8
        Entry(259, TypeShort, 1, 1u);                    // Compression = none
        Entry(262, TypeShort, 1, 2u);                    // Photometric = RGB
        Entry(273, TypeLong, 1, (uint)pixelOffset);      // StripOffsets
        Entry(277, TypeShort, 1, (uint)spp);             // SamplesPerPixel = 4
        Entry(278, TypeLong, 1, (uint)height);           // RowsPerStrip = height（单条带）
        Entry(279, TypeLong, 1, (uint)pixelBytes);       // StripByteCounts
        Entry(282, TypeRational, 1, (uint)xResOffset);         // XResolution = 72/1
        Entry(283, TypeRational, 1, (uint)yResOffset);         // YResolution = 72/1
        Entry(296, TypeShort, 1, 2u);                    // ResolutionUnit = inch
        Entry(338, TypeShort, 1, 2u);                    // ExtraSamples = 2 (unassociated alpha)

        // IFD 终止：next IFD = 0（已是 0）

        // 外部数据
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(bpsOffset), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(bpsOffset + 2), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(bpsOffset + 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(bpsOffset + 6), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(xResOffset), 72);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(xResOffset + 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(yResOffset), 72);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(yResOffset + 4), 1);

        // 像素：BGRA-premul → TIFF RGBA（关联 alpha 需 un-premul；截图不透明时 alpha=255 恒等）
        var src = bitmap.GetPixels();
        unsafe
        {
            var s = (byte*)src;
            var d = buf.AsSpan(pixelOffset);
            for (var i = 0; i < width * height * 4; i += 4)
            {
                var b = s[i];
                var g = s[i + 1];
                var r = s[i + 2];
                var a = s[i + 3];
                if (a != 255 && a != 0)
                {
                    // un-premultiply（JPEG 式四舍五入足够）
                    r = (byte)Math.Clamp(r * 255 / a, 0, 255);
                    g = (byte)Math.Clamp(g * 255 / a, 0, 255);
                    b = (byte)Math.Clamp(b * 255 / a, 0, 255);
                }
                d[i] = r;
                d[i + 1] = g;
                d[i + 2] = b;
                d[i + 3] = a;
            }
        }
        return buf;
    }
}
