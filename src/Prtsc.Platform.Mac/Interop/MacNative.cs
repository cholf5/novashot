using System.Runtime.InteropServices;

namespace Prtsc.Platform.Mac.Interop;

internal static partial class MacLibs
{
    public const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    public const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    public const string Objc = "/usr/lib/libobjc.A.dylib";
    public const string LibSystem = "libSystem.B.dylib";
}

/// <summary>
/// CoreFoundation/CoreGraphics/Carbon/libobjc 的 P/Invoke 集。
/// 签名已于 2026-11-01 在本机（macOS 26.5, arm64）以 spike 程序逐一验证。
/// 仅在 macOS 运行时调用；CF Create/Copy 规则返回值由调用方 CFRelease。
/// </summary>
internal static unsafe partial class MacNative
{
    // ---------- dyld：非 ObjC 宿主进程必须先 dlopen(AppKit) 才能拿到 AppKit 类 ----------
    [LibraryImport(MacLibs.LibSystem, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint dlopen(string path, int mode); // RTLD_NOW = 2

    // ---------- CoreFoundation ----------
    [LibraryImport(MacLibs.CF)] internal static partial void CFRelease(nint cf);

    [LibraryImport(MacLibs.CF)] internal static partial nuint CFArrayGetCount(nint array);

    [LibraryImport(MacLibs.CF)] internal static partial nint CFArrayGetValueAtIndex(nint array, nuint idx);

    [LibraryImport(MacLibs.CF)] internal static partial nint CFDictionaryGetValue(nint dict, nint key);

    [LibraryImport(MacLibs.CF)]
    internal static partial nint CFStringCreateWithCString(nint alloc, nint cStr, uint encoding);

    [LibraryImport(MacLibs.CF)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFNumberGetValue(nint number, int theType, nint valuePtr); // kCFNumberIntType = 4

    /// <summary>kCFNumberIntType 读 int32。返回 false 表示类型不兼容。</summary>
    internal static unsafe bool CFNumberToInt(nint number, out int value)
    {
        value = 0;
        fixed (int* p = &value)
        {
            return CFNumberGetValue(number, 4, (nint)p);
        }
    }

    /// <summary>kCFNumberDoubleType(6) 读 double。</summary>
    internal static unsafe bool CFNumberToDouble(nint number, out double value)
    {
        value = 0;
        fixed (double* p = &value)
        {
            return CFNumberGetValue(number, 6, (nint)p);
        }
    }

    [LibraryImport(MacLibs.CF)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFBooleanGetValue(nint boolean, [MarshalAs(UnmanagedType.U1)] out bool value);

    internal const uint kCFStringEncodingUTF8 = 0x08000100;

    /// <summary>kCFStringEncodingUTF8 把 CFString 读进调用方缓冲。返回 false 表示无法转换。</summary>
    [LibraryImport(MacLibs.CF)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFStringGetCString(nint theString, nint buffer, nuint bufferSize, uint encoding);

    [LibraryImport(MacLibs.CF)] internal static partial nuint CFStringGetLength(nint theString);

    /// <summary>把 CFString 读成托管字符串。纯 CF 实现（CFStringGetCString），不依赖 ObjC runtime / selector
    /// ——旧实现 objc_msgSend(cfstr, UTF8String sel) 在 ObjcIds 未加载时（窗口枚举先于剪贴板触发）selector=0
    /// 跳空指针 SIGSEGV（2026-11-01 mac 冒烟 bug #4，坑 12）。</summary>
    internal static unsafe string CFStringToString(nint cfstr)
    {
        if (cfstr == nint.Zero) return string.Empty;
        var maxBytes = (nuint)CFStringGetLength(cfstr) * 3 + 1; // 与 CFStringGetMaximumSizeForEncoding(UTF8) 同式：每 UTF-16 code unit 最多 3 字节（代理对 2 unit → 4 字节 ≤ 6）
        var buffer = Marshal.AllocCoTaskMem((int)maxBytes);
        try
        {
            if (!CFStringGetCString(cfstr, buffer, maxBytes, kCFStringEncodingUTF8))
                return string.Empty;
            return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    // ---------- CoreGraphics：显示器 ----------
    [LibraryImport(MacLibs.CG)] internal static partial int CGGetActiveDisplayList(uint maxDisplays, uint[]? activeDisplays, out uint displayCount);

    [LibraryImport(MacLibs.CG)] internal static partial uint CGMainDisplayID();

    [LibraryImport(MacLibs.CG)] internal static partial uint CGDisplayPixelsWide(uint display);

    [LibraryImport(MacLibs.CG)] internal static partial uint CGDisplayPixelsHigh(uint display);

    [LibraryImport(MacLibs.CG)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGDisplayIsMain(uint display);

    /// <summary>全局坐标（左上原点）下显示器几何，单位为点。</summary>
    [LibraryImport(MacLibs.CG)] internal static partial CGRect CGDisplayBounds(uint display);

    [LibraryImport(MacLibs.CG)] internal static partial nint CGDisplayCreateImage(uint display);

    // ---------- CoreGraphics：窗口 ----------
    internal const uint kCGWindowListOptionAll = 0;
    internal const uint kCGWindowListOptionOnScreenOnly = 1;
    internal const uint kCGWindowListExcludeDesktopElements = 2;
    internal const uint kCGNullWindowID = 0;

    [LibraryImport(MacLibs.CG)] internal static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    internal const uint kCGWindowImageDefault = 0;
    internal const uint kCGWindowImageBoundsIgnoreFraming = 1 << 0;
    internal const uint kCGWindowImageShouldBeOpaque = 1 << 8;
    internal const uint kCGWindowImageBestResolution = 1 << 20;
    internal const uint kCGWindowImageNominalResolution = 1 << 21;

    /// <summary>抓取窗口图片。windowId/relativeToWindow：单窗抓图用 windowId + kCGNullWindowID。</summary>
    [LibraryImport(MacLibs.CG)] internal static partial nint CGWindowListCreateImage(CGRect screenBounds, uint option, uint windowId, uint imageOption);

    [LibraryImport(MacLibs.CG)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGRectMakeWithDictionaryRepresentation(nint dict, ref CGRect rect);

    // ---------- CoreGraphics：位图上下文（CGImage → BGRA 像素） ----------
    // 实测（macOS 26 arm64，红色/蓝色填充 + CreateImage→DrawImage 往返）：
    //   32Little | PremultipliedFirst(2) → 内存字节序 (B,G,R,A) = SKColorType.Bgra8888+Premul ✓
    //   32Little | PremultipliedLast(1)  → 通道序错乱且 premul 语义不可靠，弃用
    // 头文件语义：PremultipliedFirst=2 表示 ARGB 逻辑序，32Little 把它反转为内存 B,G,R,A。
    internal const uint kCGImageAlphaPremultipliedFirst = 2;
    internal const uint kCGBitmapByteOrder32Little = 2u << 12;
    internal const uint SkiaBgraBitmapInfo = kCGBitmapByteOrder32Little | kCGImageAlphaPremultipliedFirst;

    [LibraryImport(MacLibs.CG)] internal static partial nint CGBitmapContextCreate(nint data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, uint bitmapInfo);

    [LibraryImport(MacLibs.CG)] internal static partial void CGContextDrawImage(nint c, CGRect rect, nint image);

    [LibraryImport(MacLibs.CG)] internal static partial nint CGBitmapContextGetData(nint c);

    [LibraryImport(MacLibs.CG)] internal static partial nuint CGBitmapContextGetBytesPerRow(nint c);

    [LibraryImport(MacLibs.CG)] internal static partial nuint CGImageGetWidth(nint image);

    [LibraryImport(MacLibs.CG)] internal static partial nuint CGImageGetHeight(nint image);

    [LibraryImport(MacLibs.CG)] internal static partial nint CGColorSpaceCreateDeviceRGB();

    // ---------- Carbon 全局热键 ----------
    internal const uint kEventClassKeyboard = 0x686F746B; // 'hotk'
    internal const uint kEventHotKeyPressed = 6;
    internal const uint kEventHotKeyReleased = 7;

    // modifiers（Carbon 语义，非 VK MOD_*）
    internal const uint cmdKey = 1 << 8;       // 256
    internal const uint shiftKey = 1 << 9;     // 512
    internal const uint alphaShift = 1 << 10;  // 1024（caps lock）
    internal const uint optionKey = 1 << 11;   // 2048
    internal const uint controlKey = 1 << 12;  // 4096

    [LibraryImport(MacLibs.Carbon)] internal static partial int RegisterEventHotKey(uint virtualKeyCode, uint modifiers, EventHotKeyID hotKeyID, nint target, uint options, out nint eventHotKeyRef);

    [LibraryImport(MacLibs.Carbon)] internal static partial int UnregisterEventHotKey(nint hotKeyRef);

    [LibraryImport(MacLibs.Carbon)] internal static partial nint GetApplicationEventTarget();

    [LibraryImport(MacLibs.Carbon)] internal static partial nint InstallEventHandler(nint target, delegate* unmanaged<nint, nint, void*, int> handler, nuint count, EventTypeSpec* types, void* userData, out nint handlerRef);

    [LibraryImport(MacLibs.Carbon)] internal static partial int RemoveEventHandler(nint handlerRef);

    [LibraryImport(MacLibs.Carbon)] internal static partial OSStatus GetEventParameter(nint evt, uint name, uint desiredType, out uint actualType, nuint size, out nuint actualSize, out EventHotKeyID value);

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventTypeSpec { public uint eventClass; public uint eventKind; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventHotKeyID { public uint signature; public uint id; }

    internal enum OSStatus : int { NoErr = 0 }

    // ---------- libobjc（NSPasteboard / NSString 最小面） ----------
    [LibraryImport(MacLibs.Objc, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint objc_getClass(string name);

    [LibraryImport(MacLibs.Objc, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint sel_registerName(string sel);

    [LibraryImport(MacLibs.Objc)] internal static partial nint objc_msgSend(nint receiver, nint sel);

    [LibraryImport(MacLibs.Objc)] internal static partial nint objc_msgSend(nint receiver, nint sel, nint arg1);

    [LibraryImport(MacLibs.Objc)] internal static partial nint objc_msgSend(nint receiver, nint sel, nint arg1, nint arg2);

    [LibraryImport(MacLibs.Objc)] internal static partial nint objc_msgSend(nint receiver, nint sel, nint arg1, nint arg2, nint arg3);

    [LibraryImport(MacLibs.Objc)] internal static partial nint objc_msgSend(nint receiver, nint sel, byte* arg1);

    /// <summary>手动引用计数下的配对释放：仅用于 alloc/init/copy/mutableCopy/new 系返回值。
    /// 便利构造器（autorelease）绝不能用这个，会过释放。</summary>
    [LibraryImport(MacLibs.Objc, EntryPoint = "objc_release")] internal static partial void objc_release(nint obj);

    /// <summary>+1 retain。用于把 autorelease 返回值转成 owned 所有权。</summary>
    [LibraryImport(MacLibs.Objc, EntryPoint = "objc_retain")] internal static partial nint objc_retain(nint obj);

    /// <summary>11 参 objc_msgSend（NSBitmapImageRep init 等长 selector）。调用方保证签名匹配。</summary>
    [DllImport(MacLibs.Objc, EntryPoint = "objc_msgSend")]
    internal static extern nint objc_msgSend11(nint receiver, nint sel,
        nint planes, nuint wide, nuint high, nuint bps, nuint spp,
        byte hasAlpha, byte isPlanar, nint colorSpaceName, nuint rowBytes, nuint bitsPerPixel);

    // ---------- libobjc：NSWindow chrome（overlay 铺满整屏） ----------
    // 探针实测（2026-11-01, macOS 26 arm64）：arm64 不走 objc_msgSend_stret，
    // struct 返回/传参直接走 objc_msgSend；bool 参数默认 marshalling 落 w 寄存器，均可用。

    /// <summary>objc_msgSend 返回 CGRect（frame/visibleFrame 等 getter）。</summary>
    [DllImport(MacLibs.Objc, EntryPoint = "objc_msgSend")]
    internal static extern CGRect objc_msgSendRect(nint receiver, nint sel);

    /// <summary>objc_msgSend(CGRect, bool)：setFrame:display:。</summary>
    [DllImport(MacLibs.Objc, EntryPoint = "objc_msgSend")]
    internal static extern void objc_msgSendSetFrame(nint receiver, nint sel, CGRect frame, bool display);
}

[StructLayout(LayoutKind.Sequential)]
public struct CGPoint { public double X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct CGSize { public double Width, Height; }

/// <summary>CG 几何：全局坐标左上原点（与 Windows 虚拟桌面语义一致），单位点。测试与纯换算层共用。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;

    public CGRect(double x, double y, double w, double h)
    {
        Origin = new CGPoint { X = x, Y = y };
        Size = new CGSize { Width = w, Height = h };
    }

    public static CGRect Null => new(0, 0, 0, 0);
}
