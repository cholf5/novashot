using Novashot.Core.Common;

namespace Novashot.Core.Windows;

/// <summary>
/// 单个可捕获窗口的元数据。<see cref="Handle"/> 是原生 HWND，仅在 Windows 语义上有意义。
/// Bounds 采用物理像素，去除 DWM 阴影后（可用 <c>DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)</c>）。
/// </summary>
public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    PixelRect Bounds,
    bool IsMinimized);
