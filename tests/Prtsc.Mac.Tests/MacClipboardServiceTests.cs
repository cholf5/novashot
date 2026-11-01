using Prtsc.Platform.Mac.Clipboard;
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
