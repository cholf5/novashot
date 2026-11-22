using System.Runtime.InteropServices;

namespace Novashot.Platform.Mac.Interop;

/// <summary>
/// CGWindowList 字典 key（kCGWindow* CFString 常量）的懒加载缓存。
/// 这些常量是 CoreGraphics 导出的全局 CFString；用 CFBundleGetDataPointerForName? 不必——
/// 它们其实不是导出符号，官方取法是写死字符串自建 CFString（与 Apple sample 一致：key 值即字符串字面量）。
/// </summary>
internal static class WindowKeys
{
    private static readonly object _lock = new();
    private static bool _loaded;

    internal static nint Number { get; private set; }
    internal static nint OwnerName { get; private set; }
    internal static nint WindowName { get; private set; }
    internal static nint Bounds { get; private set; }
    internal static nint Layer { get; private set; }
    internal static nint Alpha { get; private set; }
    internal static nint IsOnscreen { get; private set; }
    internal static nint OwnerPid { get; private set; }

    internal static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            Number = Create("kCGWindowNumber");
            OwnerName = Create("kCGWindowOwnerName");
            WindowName = Create("kCGWindowName");
            Bounds = Create("kCGWindowBounds");
            Layer = Create("kCGWindowLayer");
            Alpha = Create("kCGWindowAlpha");
            IsOnscreen = Create("kCGWindowIsOnscreen");
            OwnerPid = Create("kCGWindowOwnerPID");
        }
    }

    private static nint Create(string s)
    {
        var tmp = Marshal.StringToCoTaskMemUTF8(s);
        try
        {
            return MacNative.CFStringCreateWithCString(nint.Zero, tmp, 0x08000100 /* kCFStringEncodingUTF8 */);
        }
        finally
        {
            Marshal.FreeCoTaskMem(tmp);
        }
    }
}
