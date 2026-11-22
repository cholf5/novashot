using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Capture;

/// <summary>
/// <see cref="IMacDisplayApi"/> 的真实现，直接调 <see cref="MacNative"/>。
/// 仅在 macOS 主线程/任意线程可用（CG 是线程安全的）。
/// </summary>
public sealed class CGDisplayApi : IMacDisplayApi
{
    static CGDisplayApi() => WindowKeys.EnsureLoaded();

    public MacDisplayRaw[] EnumerateDisplays()
    {
        var err = MacNative.CGGetActiveDisplayList(0, null, out var count);
        if (err != 0 || count == 0) return [];

        var ids = new uint[count];
        err = MacNative.CGGetActiveDisplayList(count, ids, out _);
        if (err != 0) return [];

        var result = new MacDisplayRaw[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            var bounds = MacNative.CGDisplayBounds(id);
            result[i] = new MacDisplayRaw(
                id,
                MacNative.CGDisplayPixelsWide(id),
                MacNative.CGDisplayPixelsHigh(id),
                bounds,
                MacNative.CGDisplayIsMain(id));
        }
        return result;
    }

    public nint CreateDisplayImage(uint displayId) => MacNative.CGDisplayCreateImage(displayId);

    public nint CreateWindowImage(CGRect screenBounds, uint windowId) =>
        MacNative.CGWindowListCreateImage(screenBounds, MacNative.kCGWindowImageBestResolution, windowId, MacNative.kCGWindowImageDefault);

    public bool TryGetWindowBounds(uint windowId, out CGRect bounds)
    {
        bounds = default;
        var list = MacNative.CGWindowListCopyWindowInfo(MacNative.kCGWindowListOptionAll, MacNative.kCGNullWindowID);
        if (list == nint.Zero) return false;
        try
        {
            var n = MacNative.CFArrayGetCount(list);
            for (nuint i = 0; i < n; i++)
            {
                var dict = MacNative.CFArrayGetValueAtIndex(list, i);
                var numRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Number);
                if (numRef == nint.Zero) continue;
                if (!MacNative.CFNumberToInt(numRef, out var id) || id != windowId) continue;

                var boundsRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Bounds);
                if (boundsRef == nint.Zero) return false;
                var rect = CGRect.Null;
                if (!MacNative.CGRectMakeWithDictionaryRepresentation(boundsRef, ref rect)) return false;
                bounds = rect;
                return true;
            }
            return false;
        }
        finally
        {
            MacNative.CFRelease(list);
        }
    }
}
