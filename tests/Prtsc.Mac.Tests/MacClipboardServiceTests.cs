using Prtsc.Platform.Mac.Clipboard;
using Prtsc.Platform.Mac.Interop;
using SkiaSharp;
using Xunit;

namespace Prtsc.Mac.Tests;

/// <summary>
/// 真剪贴板 Manual 用例共用一把串行锁（xunit collection）：NSPasteboard 是全局单例，
/// 并行类互相覆盖板面会让读断言互相踩（B-8 排障实测：畸形 PNG 用例读到了并行
/// Image_Roundtrip 刚写的真图）。凡是写真板/读真板的类都挂进来。
/// </summary>
[CollectionDefinition("RealPasteboard")]
public class RealPasteboardCollection;

public class MacClipboardServiceTests
{
    [Fact]
    public void TrySetText_Empty_ReturnsFalse()
    {
        var svc = new MacClipboardService();
        Assert.False(svc.TrySetText(""));
        Assert.False(svc.TrySetText(null!));
    }

    [Fact]
    public void TrySetImage_Null_Throws()
    {
        var svc = new MacClipboardService();
        Assert.Throws<ArgumentNullException>(() => svc.TrySetImage(null!));
    }

    /// <summary>真剪贴板往返（需要 GUI 会话；CI 无 AppKit 时失败属预期，标 Manual）。</summary>
    [Trait("Category", "Manual")]
    [Collection("RealPasteboard")]
    public class ManualRoundtrip
    {
        [Fact]
        public void Text_Roundtrip()
        {
            var svc = new MacClipboardService();
            Assert.True(svc.TrySetText("prtsc-clipboard-test"));
            Assert.Equal("prtsc-clipboard-test", svc.TryGetText());
        }

        [Fact]
        public void Image_Roundtrip()
        {
            using var bitmap = new SKBitmap(10, 10);
            bitmap.Erase(new SKColor(200, 30, 40));

            var svc = new MacClipboardService();
            Assert.True(svc.TrySetImage(bitmap));

            using var back = svc.TryGetImage();
            Assert.NotNull(back);
            Assert.Equal(10, back!.Width);
            var px = back.GetPixel(5, 5);
            Assert.Equal(200, px.Red);
            Assert.Equal(30, px.Green);
            Assert.Equal(40, px.Blue);
        }
    }
}

/// <summary>
/// B-8：畸形载荷防护。剪贴板载荷是跨应用不可信数据，本类把原始畸形字节直接写进真剪贴板
/// public.png / public.tiff（模拟恶意/损坏的跨应用粘贴），锁 TryGetImage 不带异常出服务边界。
/// 修复前 public.png 载荷直解 SKBitmap.Decode：SkiaSharp 3.119.4 对畸形字节抛
/// ArgumentNullException('codec') 而非返 null（B-1 实证，B-8 探针在服务级复现）。
/// 需 GUI 会话写真剪贴板；CI 无 AppKit 时失败属预期，标 Manual（与 ManualRoundtrip 同理）。
/// 注：零长度载荷不进本测试——PasteboardDataBytes 对 length==0 短路返 null，碰不到解码层
///（空数组等价断言由 Core PngCodecTests.Decode_Garbage_ReturnsNull 钉在解码层）。
/// </summary>
[Trait("Category", "Manual")]
[Collection("RealPasteboard")]
public class MacClipboardMalformedPayloadTests
{
    [Fact]
    public void TryGetImage_MalformedPng_ReturnsNullNotThrow()
    {
        EnsureAppKitLoaded(); // ObjcIds 的 selector/UTI 句柄是懒加载的，写板前必须先装配（否则 msgSend 撞零选择子）

        using var src = new SKBitmap(4, 4);
        src.Erase(SKColors.Red);
        using var encoded = src.Encode(SKEncodedImageFormat.Png, 100);
        var realPng = encoded!.ToArray();

        WriteGarbageAndAssertNull(ObjcIds.PngType, new[]
        {
            new byte[] { 1, 2, 3, 4, 5, 6 },                      // 垃圾
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, // 仅魔数
            realPng.AsSpan(0, realPng.Length / 2).ToArray(),      // 截断 IDAT（真实 PNG 砍半）
        });
    }

    [Fact]
    public void TryGetImage_MalformedTiff_DoesNotThrowOrCrash()
    {
        EnsureAppKitLoaded();
        // TIFF 读路径走 NSBitmapImageRep(data:)（Skia 无 TIFF codec）。实测（B-8 探针，macOS 26.5）：
        // - 纯垃圾/残渣字节：绝大多数 null；ImageIO 偶发"编"出 rep（探针见过编造 800x600 的
        //   垃圾位图）。编造尺寸不可控 → 这里只锁"不抛异常不崩"，null 或垃圾位图都算降级成功。
        // - 构造真 IFD + 天文数字几何（宽/高 0x7FFF0000²、0x10000²）：实测 ImageIO 拒绝（null）；
        //   即便哪天放行，IsPixelCopySafe 守卫钉死 → 恒 null，可严格断言。
        var svc = new MacClipboardService();
        try
        {
            WriteRawPasteboard(ObjcIds.TiffType, new byte[] { 0x49, 0x49, 0x2A, 0x00 });      // 仅 II*\0 魔数
            using (svc.TryGetImage()) { }

            WriteRawPasteboard(ObjcIds.TiffType, new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 1, 2, 3, 4 }); // MM\0* + 垃圾 IFD
            using (svc.TryGetImage()) { }

            WriteRawPasteboard(ObjcIds.TiffType, CraftedTiff(0x7FFF0000, 0x7FFF0000));        // 天文数字宽高
            using (var bmp = svc.TryGetImage()) { Assert.Null(bmp); }

            WriteRawPasteboard(ObjcIds.TiffType, CraftedTiff(0x10000, 0x10000));              // 0x10000² 像素
            using (var bmp = svc.TryGetImage()) { Assert.Null(bmp); }
        }
        finally
        {
            ClearPasteboard();
        }
    }

    /// <summary>
    /// ObjcIds 的类/selector/UTI 句柄全靠 EnsureLoaded 懒加载：未装配时是 nint.Zero，
    /// 拿去 objc_msgSend 即 PC=0 段错误（B-8 排障中亲手踩过——testhost 三连崩的根因）。
    /// </summary>
    private static void EnsureAppKitLoaded() =>
        Assert.True(ObjcIds.EnsureLoaded(), "AppKit 不可用（无 GUI 会话），Manual 用例预期失败");

    /// <summary>构造最小恶意 TIFF：II*\0 + IFD@8，仅 ImageWidth/ImageLength 两个 LONG 条目（无其余 tag）。</summary>
    private static byte[] CraftedTiff(ulong w, ulong h)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'I'); bw.Write((byte)'I'); bw.Write((short)42); bw.Write(8);
        bw.Write((short)2); // entry count
        bw.Write((short)0x0100); bw.Write((short)4); bw.Write(1); bw.Write((uint)w); // ImageWidth
        bw.Write((short)0x0101); bw.Write((short)4); bw.Write(1); bw.Write((uint)h); // ImageLength
        return ms.ToArray();
    }

    /// <summary>逐个把畸形字节写上真剪贴板 → TryGetImage 必须 null 收场不抛；结束清板不留垃圾给其他应用。</summary>
    private static void WriteGarbageAndAssertNull(nint type, byte[][] payloads)
    {
        try
        {
            var svc = new MacClipboardService();
            foreach (var bytes in payloads)
            {
                WriteRawPasteboard(type, bytes);
                using var bmp = svc.TryGetImage(); // 修复前：ArgumentNullException('codec') 出服务边界
                Assert.Null(bmp);
            }
        }
        finally
        {
            ClearPasteboard();
        }
    }

    /// <summary>owned NSData（alloc/init + release，遵循 MacClipboardService 类注释的内存所有权规则）写指定类型。</summary>
    private static unsafe void WriteRawPasteboard(nint type, byte[] bytes)
    {
        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);
        fixed (byte* p = bytes)
        {
            var data = MacNative.objc_msgSend(
                MacNative.objc_msgSend(MacNative.objc_getClass("NSData"), MacNative.sel_registerName("alloc")),
                ObjcIds.InitWithBytesLengthSel, (nint)p, (nint)bytes.Length);
            MacNative.objc_msgSend(pb, ObjcIds.SetDataForTypeSel, data, type);
            MacNative.objc_release(data); // 剪贴板已持有自己的引用
        }
    }

    private static void ClearPasteboard()
    {
        var pb = MacNative.objc_msgSend(ObjcIds.NSPasteboardClass, ObjcIds.GeneralPasteboardSel);
        MacNative.objc_msgSend(pb, ObjcIds.ClearContentsSel);
    }
}

/// <summary>
/// B-8：TIFF 像素拷贝几何守卫决策表（纯函数，无需 AppKit）。DecodeTiffToBitmap 的宽/高/行宽
/// 全来自不可信载荷——ImageIO 对畸形头可能"编"出天文数字 rep（B-8 探针 + 两次 testhost
/// SIGSEGV 实证）：SKBitmap 分配失败时 GetPixels 为 null，拷贝首写 d[0] 即段错误，
/// 托管 try/catch 拦不住，必须在进拷贝循环前用几何决策钉死。
/// </summary>
public class MacClipboardServicePixelGuardTests
{
    [Fact]
    public void NonPositiveDimensions_Rejected()
    {
        Assert.False(MacClipboardService.IsPixelCopySafe(0, 10, 40));   // 宽 0
        Assert.False(MacClipboardService.IsPixelCopySafe(10, 0, 40));   // 高 0
        Assert.False(MacClipboardService.IsPixelCopySafe(-1, 10, 40));  // 负宽（NSInteger 可为负）
        Assert.False(MacClipboardService.IsPixelCopySafe(10, -1, 40));  // 负高
    }

    [Fact]
    public void RowBytesBelowRgba8Floor_Rejected()
    {
        Assert.False(MacClipboardService.IsPixelCopySafe(10, 10, 39));  // < 10*4（RGBA8 行宽下限）
        Assert.False(MacClipboardService.IsPixelCopySafe(10, 10, 0));   // 行宽 0
        Assert.False(MacClipboardService.IsPixelCopySafe(10, 10, -40)); // 负行宽
    }

    [Fact]
    public void PixelCountOverCap_Rejected()
    {
        // B-8 探针构造的恶意 IFD：0x7FFF0000² 天文数字；0x10000² ≈ 4G 像素（BGRA ~17GB 分配）
        Assert.False(MacClipboardService.IsPixelCopySafe(0x7FFF0000, 0x7FFF0000, unchecked((nint)(0x7FFF0000L * 4))));
        Assert.False(MacClipboardService.IsPixelCopySafe(0x10000, 0x10000, (nint)(0x10000L * 4)));
    }

    [Fact]
    public void SingleEdgeOverCap_RejectedBeforeMultiply()
    {
        // 单边先拦：NSInteger 全 64 位，直接乘会在 checked 外回绕绕过比较
        //（0x20000000000 * 2 回绕到小值；w*4 也回绕到负数放过 bpr 检查）
        Assert.False(MacClipboardService.IsPixelCopySafe(unchecked((nint)0x20000000000L), 2, unchecked((nint)(1L << 40))));
        Assert.False(MacClipboardService.IsPixelCopySafe(2, unchecked((nint)0x20000000000L), unchecked((nint)(1L << 40))));
        Assert.False(MacClipboardService.IsPixelCopySafe(0x40000000, 1, unchecked((nint)(1L << 34)))); // 单边 > 1e8
    }

    [Fact]
    public void RowBytesAboveCeiling_Rejected()
    {
        // 行宽上限 w*16：放行 16bit/通道 + 对齐 padding，再往上拒（与宽不匹配的怪行宽）
        Assert.False(MacClipboardService.IsPixelCopySafe(100, 10, (nint)(100L * 16 + 4096)));
        Assert.True(MacClipboardService.IsPixelCopySafe(100, 10, (nint)(100L * 16)));       // 恰在上限
        Assert.False(MacClipboardService.IsPixelCopySafe(1, 10, unchecked((nint)(1L << 40))));         // 1 像素行配 1TB 行宽
    }

    [Fact]
    public void NormalGeometry_Accepted()
    {
        Assert.True(MacClipboardService.IsPixelCopySafe(800, 600, 800 * 4));          // 典型剪贴板位图
        Assert.True(MacClipboardService.IsPixelCopySafe(5120, 2880, 5120 * 4 + 64));  // 5K 屏 + 行尾 padding
    }
}

[Trait("Category", "Manual")]
[Collection("RealPasteboard")]
public class MacClipboardPoolStressTests
{
    /// <summary>
    /// 复现 2026-11-01 用户崩溃： autorelease NSData 写剪贴板 → pool drain 释放 → 剪贴板悬垂
    /// → 下一轮 clearContents SIGSEGV。修复后（owned NSData）5 轮 + 读回必须全过。
    /// </summary>
    [Fact]
    public unsafe void TrySetImage_SurvivesAutoreleasePoolDrain_Cycles()
    {
        var svc = new MacClipboardService();
        using var bmp = new SKBitmap(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 30, 40));

        for (var round = 0; round < 5; round++)
        {
            var pool = MacNative.objc_msgSend(
                MacNative.objc_msgSend(MacNative.objc_getClass("NSAutoreleasePool"), MacNative.sel_registerName("alloc")),
                MacNative.sel_registerName("init"));

            var ok = svc.TrySetImage(bmp);
            Assert.True(ok, $"round {round} TrySetImage failed");

            MacNative.objc_msgSend(pool, MacNative.sel_registerName("drain"));
            Thread.Sleep(50);
        }

        // 最终读回验证剪贴板内容仍有效（无悬垂）
        using var back = svc.TryGetImage();
        Assert.NotNull(back);
        Assert.Equal(800, back!.Width);
        var px = back.GetPixel(400, 300);
        Assert.Equal(200, px.Red);
    }
}
