using Novashot.Core.Windows;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// B-2 WindowPicker 缩略图：等比缩放决策（纯函数，App 侧 PNG→Avalonia Bitmap 转换在 App.Tests 覆盖）。
/// 口径：装进 maxW×maxH 的等比盒子；小图不放大；输出至少 1px。
/// </summary>
public class PickerThumbnailsTests
{
    [Theory]
    [InlineData(1920, 1080, 96, 54)]   // 16:9 恰好满盒
    [InlineData(3840, 2160, 96, 54)]   // 4K 同比例
    [InlineData(1000, 1000, 54, 54)]   // 方图按高钳
    [InlineData(1080, 1920, 30, 54)]   // 竖图按高钳
    [InlineData(60, 40, 60, 40)]       // 小图不放大
    [InlineData(96, 54, 96, 54)]       // 恰好等于盒子
    [InlineData(1000, 300, 96, 29)]    // 宽钳（round 54*0.096→5? 300*96/1000=28.8→29）
    public void FitSize_AspectFit_NoUpscale(int srcW, int srcH, int expW, int expH)
    {
        Assert.Equal((expW, expH), PickerThumbnails.FitSize(srcW, srcH, 96, 54));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-3, 5)]
    public void FitSize_DegenerateSource_ReturnsOnePx(int w, int h)
    {
        Assert.Equal((1, 1), PickerThumbnails.FitSize(w, h, 96, 54));
    }

    [Fact]
    public void Resize_ReturnsFittedBitmap_LeavesSourceUnmodified()
    {
        using var src = MakeBitmap(64, 48, SKColors.DimGray);
        using var outBmp = PickerThumbnails.Resize(src, 32, 32);

        Assert.Equal(32, outBmp.Width);
        Assert.Equal(24, outBmp.Height);
        Assert.Equal(64, src.Width); // 源不被改动
    }

    [Fact]
    public void Resize_SmallerThanBox_NoUpscale()
    {
        using var src = MakeBitmap(8, 6, SKColors.DimGray);
        using var outBmp = PickerThumbnails.Resize(src, 96, 54);

        Assert.Equal(8, outBmp.Width);
        Assert.Equal(6, outBmp.Height);
    }

    private static SKBitmap MakeBitmap(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }
}
