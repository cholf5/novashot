using Novashot.Core.Clipboard;

namespace Novashot.Core.Tests;

/// <summary>
/// B-1：剪贴板位图格式决策表测试。锁定：
/// - 读序：CF_DIBV5 > CF_DIB > 注册格式 "PNG"（DIB 保真优先；PNG 只兜浏览器/Electron 只放 PNG 字节的源）
/// - 只收录探测到可用的格式（不可用格式不进候选，平台层不做空探测）
/// - 写侧互操作编码阈值（I-1）：像素 ≤4MP 才同步编码 PNG（UI 线程），超界只放 DIB 双格式
/// </summary>
public class ClipboardImageFormatsTests
{
    [Fact]
    public void SelectReadOrder_AllAvailable_DibV5FirstDibSecondPngLast()
    {
        var order = ClipboardImageFormats.SelectReadOrder(
            dibV5Available: true, dibAvailable: true, pngAvailable: true);
        Assert.Equal(
            new[] { ClipboardImageFormat.DibV5, ClipboardImageFormat.Dib, ClipboardImageFormat.Png },
            order);
    }

    [Fact]
    public void SelectReadOrder_PngOnly_FallsBackToPng()
    {
        // B-1 主路径：Chrome/Electron 只放 "PNG" 注册格式
        var order = ClipboardImageFormats.SelectReadOrder(
            dibV5Available: false, dibAvailable: false, pngAvailable: true);
        Assert.Equal(new[] { ClipboardImageFormat.Png }, order);
    }

    [Fact]
    public void SelectReadOrder_DibAndPng_PngTrailsDib()
    {
        // PNG 排在 DIB 之后：正常 DIB 命中即返回；只有 DIB 缺失或解码失败才轮到 PNG
        var order = ClipboardImageFormats.SelectReadOrder(
            dibV5Available: false, dibAvailable: true, pngAvailable: true);
        Assert.Equal(new[] { ClipboardImageFormat.Dib, ClipboardImageFormat.Png }, order);
    }

    [Fact]
    public void SelectReadOrder_DibV5Only_TakesV5Alone()
    {
        var order = ClipboardImageFormats.SelectReadOrder(
            dibV5Available: true, dibAvailable: false, pngAvailable: false);
        Assert.Equal(new[] { ClipboardImageFormat.DibV5 }, order);
    }

    [Fact]
    public void SelectReadOrder_NoneAvailable_Empty()
    {
        Assert.Empty(ClipboardImageFormats.SelectReadOrder(
            dibV5Available: false, dibAvailable: false, pngAvailable: false));
    }

    // ---------- 写侧互操作编码阈值（I-1） ----------

    [Fact]
    public void InteropEncodeMaxPixels_IsFourMegapixels()
    {
        // 钉住常数本身：QA-1 TC14 文案与代码阈值不许无声漂移
        Assert.Equal(4_000_000, ClipboardImageFormats.InteropEncodeMaxPixels);
    }

    [Fact]
    public void ShouldAttemptInteropEncode_BelowThreshold_True()
    {
        Assert.True(ClipboardImageFormats.ShouldAttemptInteropEncode(1920, 1080)); // 2.07MP
    }

    [Fact]
    public void ShouldAttemptInteropEncode_ExactlyAtThreshold_True()
    {
        Assert.True(ClipboardImageFormats.ShouldAttemptInteropEncode(2000, 2000)); // 4MP 恰好
    }

    [Fact]
    public void ShouldAttemptInteropEncode_JustAboveThreshold_False()
    {
        Assert.False(ClipboardImageFormats.ShouldAttemptInteropEncode(1, 4_000_001)); // 最小越界
    }

    [Fact]
    public void ShouldAttemptInteropEncode_FourK_False()
    {
        Assert.False(ClipboardImageFormats.ShouldAttemptInteropEncode(3840, 2160)); // 8.3MP
    }

    [Fact]
    public void ShouldAttemptInteropEncode_ProductOverflowsInt_False()
    {
        // 60000*60000 = 3.6e9 > int.MaxValue：乘积必须按 long 算——int 溢出回绕成负数会误判 true
        Assert.False(ClipboardImageFormats.ShouldAttemptInteropEncode(60000, 60000));
    }
}
