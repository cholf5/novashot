using System.Runtime.InteropServices;
using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Windowing;

/// <summary>
/// CGWindowListCopyWindowInfo 真实现。层 0 才是普通窗口；标题需要屏幕录制权限，无权限时为空串。
/// </summary>
public sealed class CGWindowListApi : IWindowListApi
{
    static CGWindowListApi() => WindowKeys.EnsureLoaded();

    public WinListRow[] CopyWindowList()
    {
        // All（含离屏/最小化），排桌面元素
        var list = MacNative.CGWindowListCopyWindowInfo(
            MacNative.kCGWindowListOptionAll | MacNative.kCGWindowListExcludeDesktopElements,
            MacNative.kCGNullWindowID);
        if (list == nint.Zero) return [];
        try
        {
            var n = MacNative.CFArrayGetCount(list);
            var result = new List<WinListRow>((int)n);
            for (nuint i = 0; i < n; i++)
            {
                var dict = MacNative.CFArrayGetValueAtIndex(list, i);
                if (RowFromDict(dict) is { } row) result.Add(row);
            }
            return result.ToArray();
        }
        finally
        {
            MacNative.CFRelease(list);
        }
    }

    private static WinListRow? RowFromDict(nint dict)
    {
        var idRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Number);
        if (idRef == nint.Zero || !MacNative.CFNumberToInt(idRef, out var idInt)) return null;

        var layerRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Layer);
        var layer = layerRef != nint.Zero && MacNative.CFNumberToInt(layerRef, out var layerInt) ? layerInt : 0;

        var alpha = 1.0;
        var alphaRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Alpha);
        if (alphaRef != nint.Zero && MacNative.CFNumberToDouble(alphaRef, out var a)) alpha = a;

        var onscreen = true;
        var onRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.IsOnscreen);
        if (onRef != nint.Zero) MacNative.CFBooleanGetValue(onRef, out onscreen);

        var pid = 0;
        var pidRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.OwnerPid);
        if (pidRef != nint.Zero && MacNative.CFNumberToInt(pidRef, out var p)) pid = p;

        var bounds = CGRect.Null;
        var boundsRef = MacNative.CFDictionaryGetValue(dict, WindowKeys.Bounds);
        if (boundsRef != nint.Zero)
            MacNative.CGRectMakeWithDictionaryRepresentation(boundsRef, ref bounds);

        var owner = MacNative.CFDictionaryGetValue(dict, WindowKeys.OwnerName);
        var title = MacNative.CFDictionaryGetValue(dict, WindowKeys.WindowName);

        return new WinListRow(
            unchecked((uint)idInt),
            owner != nint.Zero ? MacNative.CFStringToString(owner) : string.Empty,
            title != nint.Zero ? MacNative.CFStringToString(title) : string.Empty,
            bounds,
            layer,
            alpha,
            onscreen,
            pid);
    }
}
