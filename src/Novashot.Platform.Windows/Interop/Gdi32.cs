using System.Runtime.InteropServices;

namespace Novashot.Platform.Windows.Interop;

internal static class Gdi32
{
    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;

    public const int BI_RGB = 0;
    public const int DIB_RGB_COLORS = 0;

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool BitBlt(
        nint hdcDest, int xDest, int yDest, int w, int h,
        nint hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetDIBits(
        nint hdc, nint hbm, uint start, uint cLines,
        nint lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight; // 负值 = top-down
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        // 简化：不使用调色板
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bmiColors;
    }
}
