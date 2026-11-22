using System.Runtime.InteropServices;
using Novashot.Core.Common;
using Novashot.Platform.Windows.Interop;

namespace Novashot.Platform.Windows.Capture;

/// <summary>
/// 枚举物理显示器几何。假设进程 PerMonitorV2 DPI 感知已在 manifest 生效，
/// 故 User32/GDI 返回的坐标即物理像素。
/// </summary>
public static class VirtualDesktop
{
    public static IReadOnlyList<DisplayInfo> Enumerate()
    {
        var (displays, _) = EnumerateWithHandles();
        return displays;
    }

    /// <summary>
    /// 内部枚举：同时返回 <see cref="DisplayInfo"/> 与对应 HMONITOR，
    /// 两个列表 <b>下标对齐</b>。仅供 WGC 抓帧路径使用。
    /// </summary>
    internal static (IReadOnlyList<DisplayInfo> Displays, IReadOnlyList<nint> Handles) EnumerateWithHandles()
    {
        var displays = new List<DisplayInfo>();
        var handles = new List<nint>();
        var index = 0;

        User32.EnumDisplayMonitors(nint.Zero, nint.Zero, Callback, nint.Zero);
        return (displays, handles);

        bool Callback(nint hMonitor, nint hdcMonitor, ref User32.RECT lprcMonitor, nint dwData)
        {
            var info = new User32.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<User32.MONITORINFOEX>(),
                szDevice = string.Empty
            };
            if (!User32.GetMonitorInfoW(hMonitor, ref info))
                return true;

            var bounds = ToPixelRect(info.rcMonitor);
            var work = ToPixelRect(info.rcWork);
            var isPrimary = (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0;

            double scale = 1.0;
            if (Shcore.GetDpiForMonitor(hMonitor, Shcore.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                scale = dpiX / 96.0;

            displays.Add(new DisplayInfo(
                Index: index++,
                DeviceName: info.szDevice ?? string.Empty,
                Bounds: bounds,
                WorkArea: work,
                ScaleFactor: scale,
                IsPrimary: isPrimary));
            handles.Add(hMonitor);
            return true;
        }
    }

    public static PixelRect VirtualScreenBounds()
    {
        var x = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        var y = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        var w = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        var h = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);
        return new PixelRect(x, y, w, h);
    }

    private static PixelRect ToPixelRect(User32.RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}
