using System.Runtime.InteropServices;
using static Prtsc.Platform.Linux.Interop.XLibNative;

namespace Prtsc.Platform.Linux.Interop;

/// <summary>
/// XImage 句柄：包装 XGetImage 返回的 XImage 指针。GetPixel 读法对齐 Xlib：
/// 32bpp ZPixmap 按本机字节序逐行取 uint，再归一到 0xAARRGGBB。
/// </summary>
public sealed unsafe class XImageHandle : IXImage
{
    private readonly nint _display;
    private readonly nint _image;
    private readonly byte* _data;
    private readonly int _stride;
    private readonly int _bitsPerPixel;
    private readonly uint _redMask, _greenMask, _blueMask;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }

    internal XImageHandle(nint display, nint image, int width, int height)
    {
        _display = display;
        _image = image;
        Width = XImageAccess.ReadInt(image, XImageAccess.OffWidth);
        Height = XImageAccess.ReadInt(image, XImageAccess.OffHeight);
        _stride = XImageAccess.ReadInt(image, XImageAccess.OffBytesPerLine);
        _bitsPerPixel = XImageAccess.ReadInt(image, XImageAccess.OffBitsPerPixel);
        _redMask = (uint)XImageAccess.ReadMask(image, XImageAccess.OffRedMask);
        _greenMask = (uint)XImageAccess.ReadMask(image, XImageAccess.OffGreenMask);
        _blueMask = (uint)XImageAccess.ReadMask(image, XImageAccess.OffBlueMask);
        _data = (byte*)XImageAccess.ReadData(image);
    }

    public uint GetPixel(int x, int y)
    {
        if (_disposed || _data == null) return 0;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        if (_bitsPerPixel == 32)
        {
            var raw32 = *(uint*)(_data + y * _stride + x * 4);
            return Normalize(raw32);
        }
        if (_bitsPerPixel == 24)
        {
            var p = _data + y * _stride + x * 3;
            var raw24 = (uint)(*p | (*(p + 1) << 8) | (*(p + 2) << 16));
            return Normalize(raw24);
        }
        return 0; // 16bpp/8bpp 罕见（旧设备），不支持时给黑
    }

    /// <summary>掩码提取归一到 AARRGGBB（X11 视觉常是 BGRX 或 RGBX，掩码驱动）。</summary>
    private uint Normalize(uint raw)
    {
        var r = Extract(raw, _redMask);
        var g = Extract(raw, _greenMask);
        var b = Extract(raw, _blueMask);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    private static uint Extract(uint value, uint mask)
    {
        if (mask == 0) return 0;
        var shift = 0;
        while ((mask & 1u) == 0) { mask >>= 1; shift++; }
        var bits = 0;
        var m = mask;
        while ((m & 1u) == 1) { m >>= 1; bits++; }
        // 放大到 8bit
        var v = (value >> shift) & mask;
        return bits switch
        {
            8 => v,
            0 => 0,
            _ => (uint)(v * 255 / ((1u << bits) - 1)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        XDestroyImage(_image);
        GC.SuppressFinalize(this);
    }

    ~XImageHandle() => XDestroyImage(_image);
}

/// <summary>窗口抓图变体：XImage 销毁前先释放其来源 pixmap 所有权语义（XGetImage 已复制像素，仅需 DestroyImage）。</summary>
public sealed class PixmapImageHandle : IXImage
{
    private readonly IXImage _inner;

    public int Width => _inner.Width;
    public int Height => _inner.Height;

    internal PixmapImageHandle(nint display, nint image, nint pixmap, int width, int height)
    {
        // XGetImage 从 pixmap 复制像素到 XImage；pixmap 已在调用方 Free，这里只包 XImage
        _inner = new XImageHandle(display, image, width, height);
    }

    public uint GetPixel(int x, int y) => _inner.GetPixel(x, y);

    public void Dispose() => _inner.Dispose();
}

internal static partial class XImageAccess
{
    /// <summary>
    /// XImage 结构体字段偏移（Xlib.h 定义， decades 稳定）：
    /// width/height(int, 40/44)、xoffset(int 48)、byte_order(int 52)、bitmap_unit(56)、
    /// bitmap_bit_order(60)、bitmap_pad(64)、depth(68)、bytes_per_line(72)、
    /// bits_per_pixel(76)、red_mask(80)、green_mask(84)、blue_mask(88)、data(pointer 96)。
    /// 64 位下指针在 96（8 对齐）。读错只会得到黑图/崩溃，故先探针校验再启用（见 XLibApi.GetImage）。
    /// </summary>
    internal const int OffWidth = 40;
    internal const int OffHeight = 44;
    internal const int OffBytesPerLine = 72;
    internal const int OffBitsPerPixel = 76;
    internal const int OffRedMask = 80;
    internal const int OffGreenMask = 84;
    internal const int OffBlueMask = 88;
    internal const int OffData = 96;

    internal static int ReadInt(nint image, int offset) => Marshal.ReadInt32(image, offset);
    internal static int ReadMask(nint image, int offset) => Marshal.ReadInt32(image, offset);
    internal static nint ReadData(nint image) => Marshal.ReadIntPtr(image, OffData);
}
