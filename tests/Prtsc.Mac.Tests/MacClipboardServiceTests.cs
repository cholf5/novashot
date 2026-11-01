using Prtsc.Platform.Mac.Clipboard;
using Prtsc.Platform.Mac.Interop;
using SkiaSharp;
using Xunit;

namespace Prtsc.Mac.Tests;

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

public class MacClipboardPoolStressTests
{
    /// <summary>
    /// 复现 2026-11-01 用户崩溃： autorelease NSData 写剪贴板 → pool drain 释放 → 剪贴板悬垂
    /// → 下一轮 clearContents SIGSEGV。修复后（owned NSData）5 轮 + 读回必须全过。
    /// </summary>
    [Trait("Category", "Manual")]
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
