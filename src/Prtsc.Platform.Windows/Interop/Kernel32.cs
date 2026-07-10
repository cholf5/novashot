using System.Runtime.InteropServices;

namespace Prtsc.Platform.Windows.Interop;

internal static class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandleW(string? lpModuleName);
}
