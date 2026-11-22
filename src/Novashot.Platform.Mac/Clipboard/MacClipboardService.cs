using System.Runtime.InteropServices;
using SkiaSharp;
using Novashot.Core.Clipboard;
using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Clipboard;

/// <summary>
/// NSPasteboard 实现。图片写 TIFF（NSBitmapImageRep 编码，最通用）+ PNG 双格式；
/// 读图 TIFF 优先 PNG。文本 public.utf8-plain-text。快照语义：写入即拷贝。
/// 依赖 AppKit 类：先 <see cref="ObjcIds.EnsureLoaded"/>（dotnet 宿主必须 dlopen AppKit）。
///
/// ⚠ ObjC 内存所有权规则（2026-11-01 复现崩溃后定案，勿"优化"）：
/// dotnet 宿主下 ObjC runtime 是手动引用计数（非 ARC）：
/// - alloc/init 系对象：必须配对 objc_release；
/// - 便利构造器（stringWithUTF8String:、dataWithBytes:length:、TIFFRepresentation、
///   dataForType:、generalPasteboard 等非 new/copy/mutableCopy 前缀）：返回 autorelease 对象，
///   注册进当前线程 NSAutoreleasePool，**绝不能手动 release**——
///   违反即 pool drain 时过释放 → SIGSEGV（objc_release 悬垂指针）。
/// - 本类所有 objc 对象都是即用即弃，不跨 pool 边界持有，因此 autorelease 语义天然安全。
/// - 托管侧 CFRelease 仅用于 CF Create/Copy 规则对象（本类无）。
/// </summary>
public sealed class MacClipboardService : IClipboardService
{
    /// <summary>AppKit 可用性。False 时全部方法安全降级（false/null）。</summary>
    private static bool _appKitReady;
    private static readonly object _initLock = new();

    private static bool EnsureAppKit()
    {
        lock (_initLock)
        {
            if (_appKitReady) return true;
            _appKitReady = ObjcIds.EnsureLoaded();
            return _appKitReady;
        }
    }

    public bool TrySetImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!EnsureAppKit()) return false;

        using var pngData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (pngData == null) return false;

        // generalPasteboard 是 autorelease 返回，不持有
        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        if (pb == nint.Zero) return false;

        MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);

        var wrote = false;
        // TIFF 优先（消费端兼容性最好）。托管编码（MiniTiffEncoder）：零 objc 对象，
        // 绕开 NSBitmapImageRep/TIFFRepresentation 的 autorelease 所有权纠缠（2026-11-01 SIGSEGV 根因）。
        try
        {
            var tiffBytes = MiniTiffEncoder.Encode(bitmap);
            var nsTiff = NSDataOwnedFromManaged(tiffBytes);
            if (nsTiff != nint.Zero)
            {
                MacNative.objc_msgSend(pb, ObjcIds.SetDataForTypeSel, nsTiff, ObjcIds.TiffType);
                MacNative.objc_release(nsTiff); // 剪贴板已持有自己的引用
                wrote = true;
            }
        }
        catch
        {
            // TIFF 失败不阻塞 PNG
        }

        // PNG 兜底。⚠ NSData 必须 owned（alloc/init）：autorelease 对象写入剪贴板后
        // 会被 pool drain 释放 → 剪贴板悬垂 → 下一轮 clearContents/读取时 SIGSEGV
        // （2026-11-01 复现：owned 模式 5 轮稳定，autorelease 模式 round1 即崩）。
        var pngSpan = pngData.Span;
        unsafe
        {
            fixed (byte* pPng = pngSpan)
            {
                var nsPng = NSDataOwnedFromBytes((nint)pPng, (nint)pngSpan.Length);
                if (nsPng != nint.Zero)
                {
                    MacNative.objc_msgSend(pb, ObjcIds.SetDataForTypeSel, nsPng, ObjcIds.PngType);
                    MacNative.objc_release(nsPng); // 剪贴板已持有自己的引用
                    wrote = true;
                }
            }
        }

        return wrote;
    }

    public SKBitmap? TryGetImage()
    {
        if (!EnsureAppKit()) return null;

        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        if (pb == nint.Zero) return null;

        // PNG：Core PngCodec.Decode。B-8：剪贴板载荷是跨应用不可信数据，SKBitmap.Decode 直解
        // 对畸形字节抛 ArgumentNullException('codec') 而非返 null（SkiaSharp 3.119.4 实证），
        // 必须在解码层转 null——继续试 TIFF，绝不带异常出服务边界
        var png = PasteboardDataBytes(pb, ObjcIds.PngType);
        if (png != null)
        {
            var bmp = PngCodec.Decode(png);
            if (bmp != null) return bmp;
        }

        // TIFF：Skia 无 TIFF codec（2026-11-01 实证）→ NSBitmapImageRep(data:) 解码后拷 bitmapData。
        // bitmapData 是 8bit RGBA（alpha last），转 Bgra8888 返回。
        var tiff = PasteboardDataBytes(pb, ObjcIds.TiffType);
        if (tiff != null)
            return DecodeTiffToBitmap(tiff);
        return null;
    }

    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!EnsureAppKit()) return false;

        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        if (pb == nint.Zero) return false;

        var nsStr = NSStringFromUtf8(text); // autorelease，不释放
        if (nsStr == nint.Zero) return false;

        MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);
        MacNative.objc_msgSend(pb, ObjcIds.SetStringForTypeSel, nsStr, ObjcIds.Utf8PlainTextType);
        return true;
    }

    /// <summary>读回文本（Fake 语义对齐，真板读 public.utf8-plain-text）。App 侧上传 URL 复制走这里。</summary>
    public string? TryGetText()
    {
        if (!EnsureAppKit()) return null;

        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        if (pb == nint.Zero) return null;

        var s = MacNative.objc_msgSend(pb, ObjcIds.StringForTypeSel, ObjcIds.Utf8PlainTextType);
        return s == nint.Zero ? null : MacNative.CFStringToString(s);
    }

    /// <summary>
    /// G-11：macOS 无 CF_HDROP 对应物（NSPasteboard 无文件列表类型可跨应用贴图）。
    /// Finder 拷贝文件走私有 file URL 类型且语义不同，暂不支持，恒返回 null。
    /// </summary>
    public string? TryGetImageFilePath() => null;

    // ---------- helpers ----------

    private static nint NSStringFromUtf8(string s)
    {
        var p = MacStrings.ToNative(s);
        if (p == nint.Zero) return nint.Zero;
        try
        {
            return MacNative.objc_msgSend(ObjcIds.NSStringClass, ObjcIds.StringWithUtf8Sel, p);
        }
        finally
        {
            MacStrings.Free(p);
        }
    }

    /// <summary>owned NSData（alloc/initWithBytes:length:）。调用方负责 objc_release。</summary>
    private static nint NSDataOwnedFromBytes(nint bytes, nint length)
    {
        var cls = MacNative.objc_getClass("NSData");
        if (cls == nint.Zero) return nint.Zero;
        var alloc = MacNative.objc_msgSend(cls, MacNative.sel_registerName("alloc"));
        if (alloc == nint.Zero) return nint.Zero;
        var data = MacNative.objc_msgSend(alloc, ObjcIds.InitWithBytesLengthSel, bytes, length);
        if (data == nint.Zero) MacNative.objc_release(alloc); // init 失败也要平衡 alloc
        return data;
    }

    /// <summary>托管 TIFF 字节 → owned NSData。调用方 objc_release。</summary>
    private static unsafe nint NSDataOwnedFromManaged(byte[] bytes)
    {
        fixed (byte* p = bytes)
        {
            return NSDataOwnedFromBytes((nint)p, (nint)bytes.Length);
        }
    }

    /// <summary>TIFF 像素拷贝上限（像素数）。约 1 亿像素（BGRA ~400MB），真实剪贴板载荷远小于此；
    /// 只拦 ImageIO"编"出的天文数字尺寸，防无谓的大分配/烧 CPU 拷贝。
    /// 同时作为单边上限：宽/高各自先与它比较再进乘法，乘法结果 ≤ 1e16，永不回绕。</summary>
    internal const long MaxTiffPixels = 100_000_000;

    /// <summary>行宽上限 = w*16。8bit RGBA 行宽下限是 w*4；w*16 放行到 16bit/通道 + 对齐 padding，
    /// 再往上（32bit/通道、或与宽完全不匹配的怪行宽）对剪贴板位图都是无意义几何，直接拒——
    /// 顺带保证拷贝循环里 h*bpr ≤ 16e8，64 位计算无回绕，读不越 src（rep 分配 = bpr*h 字节）。</summary>
    internal const long MaxTiffRowBytesOverhead = 16;

    /// <summary>
    /// B-8：不可信 TIFF 几何（pixelsWide/pixelsHigh/bytesPerRow 全来自跨应用载荷）能否安全进
    /// 像素拷贝循环。ImageIO 对畸形头可能"编"出 rep（B-8 探针实证：4 字节垃圾可编出 800x600
    /// 垃圾位图），几何不可控；一旦几何离谱，SKBitmap 分配失败时 GetPixels 为 null，
    /// 拷贝首写 d[0] 即 SIGSEGV，托管 try/catch 拦不住——必须在进拷贝循环前钉死。
    /// RGBA8（spp=4，见拷贝循环）行宽下限 bpr &gt;= w*4；像素上限顺带挡住 (int) 截断类怪尺寸。
    /// 顺序敏感：先单边上限、再乘法——NSInteger 全 64 位，直接乘会在 checked 外回绕成负数
    /// 绕过 &gt;MaxTiffPixels 比较（B-8 收尾审查发现）。</summary>
    internal static bool IsPixelCopySafe(nint w, nint h, nint bpr)
    {
        if (w <= 0 || h <= 0) return false;
        if (w > MaxTiffPixels || h > MaxTiffPixels) return false; // 单边上限先行，下面的乘法不回绕
        if (bpr < (long)w * 4) return false;
        if (bpr > (long)w * MaxTiffRowBytesOverhead) return false;
        if ((long)w * h > MaxTiffPixels) return false;
        return true;
    }

    /// <summary>TIFF bytes → NSBitmapImageRep → bitmapData（RGBA8）→ SKBitmap(Bgra8888)。
    /// 所有权：rep（alloc/init）用 objc_release；nsData 是 autorelease，不释放。
    /// B-8：几何合法性走 <see cref="IsPixelCopySafe"/>，SKBitmap 分配失败返 null，畸形载荷不崩。</summary>
    private static unsafe SKBitmap? DecodeTiffToBitmap(byte[] tiff)
    {
        var repCls = ObjcIds.NSBitmapImageRepClass;
        if (repCls == nint.Zero) return null;

        var nsData = NSDataFromManaged(tiff); // autorelease，不释放（读路径，仅当次使用）
        if (nsData == nint.Zero) return null;

        var rep = Alloc(repCls);
        if (rep == nint.Zero) return null;
        try
        {
            rep = MacNative.objc_msgSend(rep, ObjcIds.InitWithDataSel, nsData);
            if (rep == nint.Zero) return null;

            var w = MacNative.objc_msgSend(rep, ObjcIds.PixelsWideSel);
            var h = MacNative.objc_msgSend(rep, ObjcIds.PixelsHighSel);
            var bpr = MacNative.objc_msgSend(rep, ObjcIds.BytesPerRowSel);
            var src = MacNative.objc_msgSend(rep, ObjcIds.BitmapDataSel);
            // B-8：几何全来自不可信载荷，进拷贝循环前钉死（守卫依据见 IsPixelCopySafe 注释）
            if (src == nint.Zero || !IsPixelCopySafe(w, h, bpr)) return null;

            var bitmap = new SKBitmap((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (bitmap.GetPixels() == nint.Zero) return null; // 分配失败（畸形尺寸）→ null 收场，绝不裸写
            var s = (byte*)src;
            var d = (byte*)bitmap.GetPixels();
            var spp = 4; // 8bit RGBA 固定（rep 语义 alpha last）
            // 偏移一律 64 位：几何来自不可信载荷，int 乘法在 checked 外回绕成负索引 = OOB 读写
            //（B-8 收尾审查发现；守卫后 so ≤ h*bpr ≤ 16e8、doff ≤ 4e8，long 无回绕）
            for (var y = 0; y < (int)h; y++)
            {
                for (var x = 0; x < (int)w; x++)
                {
                    var so = (long)y * bpr + (long)x * spp;
                    var doff = ((long)y * w + x) * 4;
                    d[doff] = s[so + 2];     // B
                    d[doff + 1] = s[so + 1]; // G
                    d[doff + 2] = s[so];     // R
                    d[doff + 3] = s[so + 3]; // A
                }
            }
            return bitmap;
        }
        finally
        {
            MacNative.objc_release(rep);
        }
    }

    /// <summary>读剪贴板数据为托管字节。dataForType: 返回 autorelease NSData，只读不释放。</summary>
    private static byte[]? PasteboardDataBytes(nint pb, nint type)
    {
        var data = MacNative.objc_msgSend(pb, ObjcIds.DataForTypeSel, type);
        if (data == nint.Zero) return null;

        var bytes = MacNative.objc_msgSend(data, ObjcIds.BytesSel);
        var length = MacNative.objc_msgSend(data, ObjcIds.LengthSel);
        if (bytes == nint.Zero || length == 0) return null;
        var managed = new byte[(long)length];
        Marshal.Copy(bytes, managed, 0, managed.Length);
        return managed;
    }

    /// <summary>autorelease NSData（dataWithBytes:length:），读路径即用即弃，不释放。</summary>
    private static nint NSDataFromBytesAutorelease(nint bytes, nint length)
    {
        var cls = MacNative.objc_getClass("NSData");
        if (cls == nint.Zero) return nint.Zero;
        return MacNative.objc_msgSend(cls, MacNative.sel_registerName("dataWithBytes:length:"), bytes, length);
    }

    private static unsafe nint NSDataFromManaged(byte[] bytes)
    {
        var cls = MacNative.objc_getClass("NSData");
        if (cls == nint.Zero) return nint.Zero;
        fixed (byte* p = bytes)
        {
            return NSDataFromBytesAutorelease((nint)p, (nint)bytes.Length);
        }
    }

    private static nint Alloc(nint cls) =>
        MacNative.objc_msgSend(cls, MacNative.sel_registerName("alloc"));
}
