using System.Runtime.InteropServices;
using SkiaSharp;
using Prtsc.Core.Clipboard;
using Prtsc.Platform.Mac.Interop;

namespace Prtsc.Platform.Mac.Clipboard;

/// <summary>
/// NSPasteboard 实现。图片写 TIFF（NSBitmapImageRep 编码，最通用）+ PNG 双格式；
/// 读图 TIFF 优先 PNG。文本 public.utf8-plain-text。快照语义：写入即拷贝。
/// 依赖 AppKit 类：先 <see cref="ObjcIds.EnsureLoaded"/>（dotnet 宿主必须 dlopen AppKit）。
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

        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        if (pb == nint.Zero) return false;

        // PNG 兜底
        using var pngData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (pngData == null) return false;

        MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);

        var wrote = false;
        // TIFF 优先：NSBitmapImageRep(cgImage) → TIFFRepresentation；这里用像素直接构造 rep
        try
        {
            var tiff = BuildTiffData(bitmap);
            if (tiff != nint.Zero)
            {
                MacNative.objc_msgSend(pb, ObjcIds.SetDataForTypeSel, tiff, ObjcIds.TiffType);
                MacNative.CFRelease(tiff);
                wrote = true;
            }
        }
        catch
        {
            // TIFF 失败不阻塞 PNG
        }

        var pngSpan = pngData.Span;
        unsafe
        {
            fixed (byte* pPng = pngSpan)
            {
                var nsPng = NSDataFromBytes((nint)pPng, (nint)pngSpan.Length);
                if (nsPng != nint.Zero)
                {
                    MacNative.objc_msgSend(pb, ObjcIds.SetDataForTypeSel, nsPng, ObjcIds.PngType);
                    MacNative.CFRelease(nsPng);
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

        // PNG：SKBitmap.Decode 直解
        var png = PasteboardDataBytes(pb, ObjcIds.PngType);
        if (png != null)
        {
            var bmp = SKBitmap.Decode(png);
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

        var nsStr = NSStringFromUtf8(text);
        if (nsStr == nint.Zero) return false;
        try
        {
            MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);
            MacNative.objc_msgSend(pb, ObjcIds.SetStringForTypeSel, nsStr, ObjcIds.Utf8PlainTextType);
            return true;
        }
        finally
        {
            MacNative.CFRelease(nsStr);
        }
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

    /// <summary>SKBitmap → TIFF NSData。NSBitmapImageRep 的 planes=NULL 是"自分配不初始化"语义（非 copy），
    /// 必须在 init 后手动把 BGRA 像素转 RGBA 写进 bitmapData（2026-11-01 实证：不写则全 0）。</summary>
    private static unsafe nint BuildTiffData(SKBitmap bitmap)
    {
        var repCls = ObjcIds.NSBitmapImageRepClass;
        if (repCls == nint.Zero) return nint.Zero;

        var width = bitmap.Info.Width;
        var height = bitmap.Info.Height;

        var rep = Alloc(repCls);
        if (rep == nint.Zero) return nint.Zero;

        var colorSpaceName = NSStringFromUtf8("NSCalibratedRGBColorSpace");
        try
        {
            rep = MacNative.objc_msgSend11(rep, ObjcIds.InitWithBitmapDataPlanesSel, nint.Zero,
                (nuint)width, (nuint)height, 8, 4, hasAlpha: 1, isPlanar: 0, colorSpaceName,
                (nuint)(width * 4), 32);
            if (rep == nint.Zero) return nint.Zero;

            var dst = MacNative.objc_msgSend(rep, ObjcIds.BitmapDataSel);
            if (dst == nint.Zero) return nint.Zero;

            // BGRA8888-premul → RGBA8888（rep 语义 alpha last）；截图是不透明图，premul 直传通道值不变
            var src = (byte*)bitmap.GetPixels();
            var d = (byte*)dst;
            for (var i = 0; i < width * height * 4; i += 4)
            {
                d[i] = src[i + 2];     // R
                d[i + 1] = src[i + 1]; // G
                d[i + 2] = src[i];     // B
                d[i + 3] = src[i + 3]; // A
            }

            return MacNative.objc_msgSend(rep, ObjcIds.TiffRepresentationSel);
        }
        finally
        {
            MacStrings.Free(colorSpaceName);
            MacNative.CFRelease(rep);
        }
    }

    /// <summary>TIFF bytes → NSBitmapImageRep → bitmapData（RGBA8）→ SKBitmap(Bgra8888)。</summary>
    private static unsafe SKBitmap? DecodeTiffToBitmap(byte[] tiff)
    {
        var repCls = ObjcIds.NSBitmapImageRepClass;
        if (repCls == nint.Zero) return null;

        var nsData = NSDataFromManaged(tiff);
        if (nsData == nint.Zero) return null;
        try
        {
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
                if (w == 0 || h == 0 || src == nint.Zero) return null;

                var bitmap = new SKBitmap((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Premul);
                var s = (byte*)src;
                var d = (byte*)bitmap.GetPixels();
                var spp = 4; // 8bit RGBA 固定（rep 语义 alpha last）
                for (var y = 0; y < (int)h; y++)
                {
                    for (var x = 0; x < (int)w; x++)
                    {
                        var so = y * (int)bpr + x * (int)spp;
                        var doff = y * (int)w * 4 + x * 4;
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
                MacNative.CFRelease(rep);
            }
        }
        finally
        {
            MacNative.CFRelease(nsData);
        }
    }

    private static byte[]? PasteboardDataBytes(nint pb, nint type)
    {
        var data = MacNative.objc_msgSend(pb, ObjcIds.DataForTypeSel, type);
        if (data == nint.Zero) return null;
        try
        {
            var bytes = MacNative.objc_msgSend(data, ObjcIds.BytesSel);
            var length = MacNative.objc_msgSend(data, ObjcIds.LengthSel);
            if (bytes == nint.Zero || length == 0) return null;
            var managed = new byte[(long)length];
            Marshal.Copy(bytes, managed, 0, managed.Length);
            return managed;
        }
        finally
        {
            MacNative.CFRelease(data);
        }
    }

    private static nint Alloc(nint cls) =>
        MacNative.objc_msgSend(cls, MacNative.sel_registerName("alloc"));

    private static unsafe nint NSDataFromManaged(byte[] bytes)
    {
        var cls = MacNative.objc_getClass("NSData");
        if (cls == nint.Zero) return nint.Zero;
        fixed (byte* p = bytes)
        {
            return NSDataFromBytes((nint)p, (nint)bytes.Length);
        }
    }

    private static nint NSDataFromBytes(nint bytes, nint length)
    {
        var cls = MacNative.objc_getClass("NSData");
        if (cls == nint.Zero) return nint.Zero;
        return MacNative.objc_msgSend(cls, MacNative.sel_registerName("dataWithBytes:length:"), bytes, length);
    }
}

