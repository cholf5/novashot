using System.Runtime.InteropServices;

namespace Prtsc.Platform.Windows.Interop;

/// <summary>
/// dwmapi P/Invoke。用于窗口枚举的 cloaked 过滤与真实几何（去 DWM 阴影）。
/// </summary>
internal static class DwmApi
{
    // DWMWINDOWATTRIBUTE 枚举成员
    public const uint DWMWA_CLOAKED = 14;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // DWMWA_CLOAKED 返回值（cbAttribute = 4）
    public const int DWM_CLOAKED_APP = 0x0000_0001;
    public const int DWM_CLOAKED_SHELL = 0x0000_0002;
    public const int DWM_CLOAKED_INHERITED = 0x0000_0004;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        nint hwnd, uint dwAttribute, out User32.RECT pvAttribute, int cbAttribute);
}
