using System.Runtime.InteropServices;

namespace Novashot.Platform.Windows.Interop;

internal static class Shcore
{
    public enum MONITOR_DPI_TYPE
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
        MDT_DEFAULT = MDT_EFFECTIVE_DPI
    }

    [DllImport("Shcore.dll")]
    public static extern int GetDpiForMonitor(nint hMonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);
}
