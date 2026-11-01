using System.Runtime.InteropServices;

namespace Prtsc.Platform.Mac.Interop;

/// <summary>
/// AppKit / Foundation 类与 selector 的懒加载缓存。
/// 非 ObjC 宿主进程（dotnet CLI 启动）必须先 <see cref="MacNative.dlopen"/> AppKit，
/// 否则 objc_getClass 返回 0（2026-11-01 spike 实证）。
/// </summary>
internal static class ObjcIds
{
    private static readonly object _lock = new();
    private static bool _loaded;

    // 类
    internal static nint NSPasteboardClass { get; private set; }
    internal static nint NSStringClass { get; private set; }
    internal static nint NSBitmapImageRepClass { get; private set; }
    internal static nint NSWorkspaceClass { get; private set; }

    // selector
    internal static nint GeneralPasteboardSel { get; private set; }
    internal static nint ClearContentsSel { get; private set; }
    internal static nint SetStringForTypeSel { get; private set; }
    internal static nint StringForTypeSel { get; private set; }
    internal static nint Utf8StringSel { get; private set; }
    internal static nint StringWithUtf8Sel { get; private set; }
    internal static nint DataWithBytesLengthSel { get; private set; }
    internal static nint SetDataForTypeSel { get; private set; }
    internal static nint DataForTypeSel { get; private set; }
    internal static nint BytesSel { get; private set; }
    internal static nint LengthSel { get; private set; }
    internal static nint InitWithBitmapDataPlanesSel { get; private set; }
    internal static nint TiffRepresentationSel { get; private set; }

    // UTI / pasteboard type 常量（自建 CFString 字面量，不依赖 dlsym 导出）
    internal static nint Utf8PlainTextType { get; private set; }
    internal static nint PngType { get; private set; }
    internal static nint TiffType { get; private set; }

    /// <summary>幂等。失败（理论上不会）返回 false，调用方降级。</summary>
    internal static bool EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return NSPasteboardClass != nint.Zero;

            _loaded = true;
            // dotnet 宿主未初始化 ObjC runtime；dlopen 让 +initialize 按需跑
            MacNative.dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 2 /* RTLD_NOW */);

            NSPasteboardClass = MacNative.objc_getClass("NSPasteboard");
            NSStringClass = MacNative.objc_getClass("NSString");
            NSBitmapImageRepClass = MacNative.objc_getClass("NSBitmapImageRep");
            NSWorkspaceClass = MacNative.objc_getClass("NSWorkspace");

            GeneralPasteboardSel = MacNative.sel_registerName("generalPasteboard");
            ClearContentsSel = MacNative.sel_registerName("clearContents");
            SetStringForTypeSel = MacNative.sel_registerName("setString:forType:");
            StringForTypeSel = MacNative.sel_registerName("stringForType:");
            Utf8StringSel = MacNative.sel_registerName("UTF8String");
            StringWithUtf8Sel = MacNative.sel_registerName("stringWithUTF8String:");
            DataWithBytesLengthSel = MacNative.sel_registerName("dataWithBytes:length:");
            SetDataForTypeSel = MacNative.sel_registerName("setData:forType:");
            DataForTypeSel = MacNative.sel_registerName("dataForType:");
            BytesSel = MacNative.sel_registerName("bytes");
            LengthSel = MacNative.sel_registerName("length");
            InitWithBitmapDataPlanesSel = MacNative.sel_registerName("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bytesPerRow:bitsPerPixel:");
            TiffRepresentationSel = MacNative.sel_registerName("TIFFRepresentation");

            Utf8PlainTextType = StringConstant("public.utf8-plain-text");
            PngType = StringConstant("public.png");
            TiffType = StringConstant("public.tiff");

            return NSPasteboardClass != nint.Zero;
        }
    }

    private static nint StringConstant(string utf8) =>
        MacNative.objc_msgSend(MacNative.objc_getClass("NSString"), StringWithUtf8Sel, (nint)Marshal.StringToCoTaskMemUTF8(utf8));
}
