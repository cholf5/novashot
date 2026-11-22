using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Platform.Mac.Capture;
using Novashot.Platform.Mac.Interop;
using SkiaSharp;
using Xunit;

[assembly: DisableRuntimeMarshalling()]

namespace Novashot.Mac.Tests;

/// <summary>
/// Fake 显示 API：显示器/图像由测试注入，像素用可识别的纯色（每屏一色），
/// 验证 MacCaptureService 的合成/偏移/异常语义（对齐 GdiCaptureService）。
/// </summary>
internal static unsafe partial class FakeCg
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(CG)] internal static partial nint CGBitmapContextCreateImage(nint ctx);

    [LibraryImport(CG)] internal static partial nint CGColorCreate(nint space, double[] components);

    [LibraryImport(CG)] internal static partial void CGContextSetFillColorWithColor(nint c, nint color);

    [LibraryImport(CG)] internal static partial void CGContextFillRect(nint c, CGRect rect);
}
internal sealed class FakeDisplayApi : IMacDisplayApi
{
    public MacDisplayRaw[] Displays { get; set; } = [];

    /// <summary>displayId → (图像宽高物理像素, 填充色)。未配置时 CreateDisplayImage 返回 0。</summary>
    public Dictionary<uint, (int W, int H, SKColor Color)> DisplayImages { get; } = new();

    public Dictionary<uint, (int W, int H, SKColor Color)> WindowImages { get; } = new();

    public Dictionary<uint, CGRect> WindowBounds { get; } = new();

    public MacDisplayRaw[] EnumerateDisplays() => Displays;

    public nint CreateDisplayImage(uint displayId)
    {
        if (!DisplayImages.TryGetValue(displayId, out var spec)) return nint.Zero;
        return MakeSolidImage(spec.W, spec.H, spec.Color);
    }

    public nint CreateWindowImage(CGRect screenBounds, uint windowId)
    {
        if (!WindowImages.TryGetValue(windowId, out var spec)) return nint.Zero;
        return MakeSolidImage(spec.W, spec.H, spec.Color);
    }

    public bool TryGetWindowBounds(uint windowId, out CGRect bounds)
    {
        if (WindowBounds.TryGetValue(windowId, out var b)) { bounds = b; return true; }
        bounds = default;
        return false;
    }

    /// <summary>造一张纯色 CGImage。必须走 CGContextFillRect 正规绘制路径：
    /// 直接 memset context 内存不会标脏 context，CGBitmapContextCreateImage 快照会得到空图（2026-11-01 实证）。</summary>
    internal static nint MakeSolidImage(int w, int h, SKColor color)
    {
        var cs = MacNative.CGColorSpaceCreateDeviceRGB();
        var ctx = MacNative.CGBitmapContextCreate(nint.Zero, (nuint)w, (nuint)h, 8, (nuint)(w * 4), cs,
            MacNative.SkiaBgraBitmapInfo);
        // CGColorCreate 接受 RGBA 分量（非 BGRA）
        var img = FillAndSnapshot(ctx, cs, w, h, color.Red / 255.0, color.Green / 255.0, color.Blue / 255.0, color.Alpha / 255.0);
        MacNative.CFRelease(ctx);
        MacNative.CFRelease(cs);
        return img;
    }

    private static nint FillAndSnapshot(nint ctx, nint cs, int w, int h, double r, double g, double b, double a)
    {
        // CGColorCreateGenericRGB 是宏不是导出符号；用 CGColorCreate + double[]（blittable by DisableRuntimeMarshalling）
        var color = FakeCg.CGColorCreate(cs, [r, g, b, a]);
        FakeCg.CGContextSetFillColorWithColor(ctx, color);
        FakeCg.CGContextFillRect(ctx, new CGRect(0, 0, w, h));
        MacNative.CFRelease(color);
        return FakeCg.CGBitmapContextCreateImage(ctx);
    }
}

public class MacCaptureServiceTests : IDisposable
{
    private readonly FakeDisplayApi _api = new();
    private readonly MacCaptureService _svc;

    public MacCaptureServiceTests()
    {
        _svc = new MacCaptureService(_api, NullLogger<MacCaptureService>.Instance);
    }

    public void Dispose()
    {
        foreach (var id in _api.DisplayImages.Keys) CleanupImage(id);
        // 无需逐个释放：MakeSolidImage 的 CGImage 由被测代码 CFRelease；
        // 这里只需要清字典，未使用图像由 GC 兜底（测试进程退出即回收）。
        GC.SuppressFinalize(this);
    }

    private static void CleanupImage(uint _) { }

    // ---------- EnumerateDisplays ----------

    [Fact]
    public void EnumerateDisplays_SortsLeftToRight_AndAssignsIndex()
    {
        _api.Displays = [
            new(100, 1920, 1080, new CGRect(1920, 0, 1920, 1080), IsMain: false),
            new(200, 1512, 982, new CGRect(0, 0, 1512, 982), IsMain: true),
        ];
        var displays = _svc.EnumerateDisplays();

        Assert.Equal(2, displays.Count);
        Assert.Equal("CGDisplay200", displays[0].DeviceName); // 主屏在左
        Assert.Equal(0, displays[0].Index);
        Assert.Equal(1, displays[1].Index);
        Assert.True(displays[0].IsPrimary);
    }

    [Fact]
    public void EnumerateDisplays_PreviousListSurvivesReenumerationDuringCallerLoop()
    {
        // 复现 MainWindow.CaptureRegionAsync：外层 foreach displays，循环体 CaptureDisplay
        // 内部再次 EnumerateDisplays；若返回内部活缓存，第二次 MoveNext 抛 Collection modified
        _api.Displays = [
            new(1, 800, 600, new CGRect(0, 0, 800, 600), IsMain: true),
            new(2, 800, 600, new CGRect(800, 0, 800, 600), IsMain: false),
        ];
        _api.DisplayImages[1] = (800, 600, new SKColor(255, 0, 0));
        _api.DisplayImages[2] = (800, 600, new SKColor(0, 0, 255));

        var displays = _svc.EnumerateDisplays();
        var shots = new List<CapturedImage>();
        foreach (var d in displays)
            shots.Add(_svc.CaptureDisplay(d.Index));

        Assert.Equal(2, shots.Count);
        foreach (var s in shots) s.Dispose();
    }

    [Fact]
    public void EnumerateDisplays_RetinaScale_DerivedFromPixelRatio()
    {
        _api.Displays = [new(1, 3024, 1964, new CGRect(0, 0, 1512, 982), IsMain: true)];
        var displays = _svc.EnumerateDisplays();

        Assert.Equal(2.0, displays[0].ScaleFactor);
        Assert.Equal(3024, displays[0].Bounds.Width); // 物理像素
        Assert.Equal(1964, displays[0].Bounds.Height);
    }

    // ---------- CaptureDisplay ----------

    [Fact]
    public void CaptureDisplay_ReturnsSolidBitmapOfDisplaySize()
    {
        _api.Displays = [new(1, 800, 600, new CGRect(0, 0, 800, 600), IsMain: true)];
        _api.DisplayImages[1] = (800, 600, new SKColor(255, 0, 0));

        using var img = _svc.CaptureDisplay(0);
        Assert.Equal(800, img.Width);
        Assert.Equal(600, img.Height);
        Assert.Equal(CaptureSource.Display, img.Source);
        AssertPixel(img.Bitmap, 400, 300, new SKColor(255, 0, 0));
    }

    [Fact]
    public void CaptureDisplay_InvalidIndex_Throws()
    {
        _api.Displays = [new(1, 800, 600, new CGRect(0, 0, 800, 600), IsMain: true)];
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.CaptureDisplay(5));
    }

    [Fact]
    public void CaptureDisplay_CgFails_ThrowsWithPermissionHint()
    {
        _api.Displays = [new(1, 800, 600, new CGRect(0, 0, 800, 600), IsMain: true)];
        // 未配置 DisplayImages → CreateDisplayImage 返回 0
        var ex = Assert.Throws<InvalidOperationException>(() => _svc.CaptureDisplay(0));
        Assert.Contains("屏幕录制", ex.Message);
    }

    // ---------- CaptureFullscreen ----------

    [Fact]
    public void CaptureFullscreen_TwoDisplays_PixelsLaidOutByGlobalPosition()
    {
        // 左屏 100x100 红色 @ (0,0)；右屏 100x100 蓝色 @ (100,0)
        _api.Displays = [
            new(1, 100, 100, new CGRect(0, 0, 100, 100), IsMain: true),
            new(2, 100, 100, new CGRect(100, 0, 100, 100), IsMain: false),
        ];
        _api.DisplayImages[1] = (100, 100, new SKColor(255, 0, 0));
        _api.DisplayImages[2] = (100, 100, new SKColor(0, 0, 255));

        using var img = _svc.CaptureFullscreen();
        Assert.Equal(200, img.Width);
        Assert.Equal(100, img.Height);
        Assert.Equal(CaptureSource.Fullscreen, img.Source);
        AssertPixel(img.Bitmap, 50, 50, new SKColor(255, 0, 0));   // 左半红
        AssertPixel(img.Bitmap, 150, 50, new SKColor(0, 0, 255));  // 右半蓝
    }

    [Fact]
    public void CaptureFullscreen_NegativeCoordinates_SecondaryAboveMain()
    {
        // 主屏 (0,0,1920,1080)；副屏在正上方 (0,-1080)
        _api.Displays = [
            new(1, 1920, 1080, new CGRect(0, 0, 1920, 1080), IsMain: true),
            new(2, 1920, 1080, new CGRect(0, -1080, 1920, 1080), IsMain: false),
        ];
        _api.DisplayImages[1] = (1920, 1080, new SKColor(10, 200, 30));
        _api.DisplayImages[2] = (1920, 1080, new SKColor(200, 10, 30));

        using var img = _svc.CaptureFullscreen();
        Assert.Equal(1920, img.Width);
        Assert.Equal(2160, img.Height);
        Assert.Equal(0, img.Bounds.X);
        Assert.Equal(-1080, img.Bounds.Y);
        AssertPixel(img.Bitmap, 960, 540, new SKColor(200, 10, 30));   // 上半 = 副屏
        AssertPixel(img.Bitmap, 960, 1620, new SKColor(10, 200, 30));  // 下半 = 主屏
    }

    [Fact]
    public void CaptureFullscreen_NoDisplays_Throws()
    {
        _api.Displays = [];
        Assert.Throws<InvalidOperationException>(() => _svc.CaptureFullscreen());
    }

    // ---------- CaptureRegion ----------

    [Fact]
    public void CaptureRegion_SingleDisplay_CropsContent()
    {
        _api.Displays = [new(1, 1000, 1000, new CGRect(0, 0, 1000, 1000), IsMain: true)];
        // 屏幕图：左 500 列红右 500 列蓝
        _api.DisplayImages[1] = (1000, 1000, new SKColor(255, 0, 0));

        using var img = _svc.CaptureRegion(new PixelRect(400, 300, 200, 200));
        Assert.Equal(200, img.Width);
        Assert.Equal(CaptureSource.Region, img.Source);
        Assert.Equal(400, img.Bounds.X);
        AssertPixel(img.Bitmap, 100, 100, new SKColor(255, 0, 0));
    }

    [Fact]
    public void CaptureRegion_AcrossTwoDisplays_CompositesBoth()
    {
        _api.Displays = [
            new(1, 100, 100, new CGRect(0, 0, 100, 100), IsMain: true),
            new(2, 100, 100, new CGRect(100, 0, 100, 100), IsMain: false),
        ];
        _api.DisplayImages[1] = (100, 100, new SKColor(255, 0, 0));
        _api.DisplayImages[2] = (100, 100, new SKColor(0, 0, 255));

        // 跨屏 region：x 50..150
        using var img = _svc.CaptureRegion(new PixelRect(50, 25, 100, 50));
        Assert.Equal(100, img.Width);
        Assert.Equal(50, img.Height);
        AssertPixel(img.Bitmap, 20, 25, new SKColor(255, 0, 0));  // 左边来自屏 1
        AssertPixel(img.Bitmap, 80, 25, new SKColor(0, 0, 255));  // 右边来自屏 2
    }

    [Fact]
    public void CaptureRegion_Empty_Throws()
    {
        _api.Displays = [new(1, 100, 100, new CGRect(0, 0, 100, 100), IsMain: true)];
        Assert.Throws<ArgumentException>(() => _svc.CaptureRegion(default));
        Assert.Throws<ArgumentException>(() => _svc.CaptureRegion(new PixelRect(5000, 5000, 10, 10)));
    }

    // ---------- CaptureWindow ----------

    [Fact]
    public void CaptureWindow_UsesActualImageSize_AndScalesBounds()
    {
        _api.Displays = [new(1, 3024, 1964, new CGRect(0, 0, 1512, 982), IsMain: true)];
        _api.WindowBounds[42] = new CGRect(100, 200, 500, 400); // 点
        _api.WindowImages[42] = (1000, 800, new SKColor(1, 2, 3)); // BestResolution 2x

        using var img = _svc.CaptureWindow(42);
        Assert.Equal(1000, img.Width);  // 实际图像像素
        Assert.Equal(800, img.Height);
        Assert.Equal(CaptureSource.Window, img.Source);
        Assert.Equal(200, img.Bounds.X); // 100 点 × 2
        Assert.Equal(400, img.Bounds.Y);
        AssertPixel(img.Bitmap, 500, 400, new SKColor(1, 2, 3));
    }

    [Fact]
    public void CaptureWindow_UnknownWindow_Throws()
    {
        _api.Displays = [new(1, 100, 100, new CGRect(0, 0, 100, 100), IsMain: true)];
        Assert.Throws<InvalidOperationException>(() => _svc.CaptureWindow(999));
        Assert.Throws<ArgumentException>(() => _svc.CaptureWindow(nint.Zero));
    }

    // ---------- helpers ----------

    private static void AssertPixel(SKBitmap bitmap, int x, int y, SKColor expected)
    {
        var actual = bitmap.GetPixel(x, y);
        Assert.True(
            actual.Red == expected.Red && actual.Green == expected.Green && actual.Blue == expected.Blue,
            $"pixel ({x},{y}) expected {expected} got {actual}");
    }
}
