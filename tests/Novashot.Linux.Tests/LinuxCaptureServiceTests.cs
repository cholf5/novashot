using Microsoft.Extensions.Logging.Abstractions;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Platform.Linux.Capture;
using Novashot.Platform.Linux.Interop;
using SkiaSharp;

namespace Novashot.Linux.Tests;

/// <summary>
/// LinuxCaptureService 合成语义（Fake X11）：双屏拼接/偏移、单屏抓取、区域裁剪、
/// 空显示器报错、每次枚举返回新数组（MAC-1 坑 #3 回归）。
/// </summary>
public class LinuxCaptureServiceTests
{
    private static XDisplayRaw Disp(nint crtc, string name, int x, int y, int w, int h, bool primary = false) =>
        new(crtc, name, x, y, w, h, (int)(w * 25.4 / 96.0), (int)(h * 25.4 / 96.0), primary);

    private static (LinuxCaptureService Svc, FakeXLibApi Api) Make(params XDisplayRaw[] displays)
    {
        var api = new FakeXLibApi { Displays = [.. displays] };
        api.Open();
        var svc = new LinuxCaptureService(api, NullLogger<LinuxCaptureService>.Instance);
        return (svc, api);
    }

    [Fact]
    public void EnumerateDisplays_PrimaryFirst_ThenByPosition()
    {
        var (svc, _) = Make(
            Disp(0x11, "HDMI-0", 1920, 0, 1920, 1080),
            Disp(0x10, "eDP-1", 0, 0, 1920, 1080, primary: true));

        var displays = svc.EnumerateDisplays();

        Assert.Equal(2, displays.Count);
        Assert.Equal("eDP-1", displays[0].DeviceName);   // primary 在前
        Assert.Equal(0, displays[0].Index);
        Assert.Equal("HDMI-0", displays[1].DeviceName);
        Assert.True(displays[0].IsPrimary);
    }

    [Fact]
    public void EnumerateDisplays_ReturnsNewArrayEachCall()
    {
        var (svc, _) = Make(Disp(0x10, "eDP-1", 0, 0, 1920, 1080, primary: true));

        var a = svc.EnumerateDisplays();
        var b = svc.EnumerateDisplays();

        Assert.NotSame(a, b);
    }

    [Fact]
    public void CaptureFullscreen_JoinsBothDisplays_WithOffset()
    {
        var (svc, api) = Make(
            Disp(0x10, "eDP-1", 0, 0, 100, 80, primary: true),
            Disp(0x11, "HDMI-0", 100, 0, 60, 80));
        // 全局坐标给色：左屏 (x<100) 红，右屏 (x≥100) 蓝
        api.PixelSource = (x, _) => x >= 100 ? 0xFF0000FFu : 0xFFFF0000u;

        using var img = svc.CaptureFullscreen();

        Assert.Equal(160, img.Width);
        Assert.Equal(80, img.Height);
        Assert.Equal(CaptureSource.Fullscreen, img.Source);
        Assert.Equal(SKColors.Red, img.Bitmap.GetPixel(50, 40));
        Assert.Equal(SKColors.Blue, img.Bitmap.GetPixel(120, 40));
    }

    [Fact]
    public void CaptureRegion_CropsRequestedRect()
    {
        var (svc, api) = Make(Disp(0x10, "eDP-1", 0, 0, 200, 200, primary: true));
        // 全局坐标给色：只有请求区域 (50..90, 60..100) 是绿（真实现从根窗拷贝对应矩形）
        api.PixelSource = (x, y) => (x, y) switch
        {
            ( >= 50 and < 90, >= 60 and < 100) => 0xFF00FF00u,
            _ => 0xFF000000u,
        };

        using var img = svc.CaptureRegion(new PixelRect(50, 60, 40, 40));

        Assert.Equal(40, img.Width);
        Assert.Equal(40, img.Height);
        Assert.Equal(SKColors.Lime, img.Bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, img.Bitmap.GetPixel(39, 39));
    }

    [Fact]
    public void CaptureRegion_NoDisplayIntersection_Throws()
    {
        var (svc, _) = Make(Disp(0x10, "eDP-1", 0, 0, 100, 100, primary: true));
        Assert.Throws<ArgumentException>(() => svc.CaptureRegion(new PixelRect(500, 500, 50, 50)));
    }

    [Fact]
    public void CaptureFullscreen_NoDisplays_ThrowsWithHint()
    {
        var (svc, _) = Make();
        var ex = Assert.Throws<InvalidOperationException>(() => svc.CaptureFullscreen());
        Assert.Contains("DISPLAY", ex.Message);
    }

    [Fact]
    public void CaptureDisplay_ThrowsOnBadIndex()
    {
        var (svc, _) = Make(Disp(0x10, "eDP-1", 0, 0, 100, 100, primary: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.CaptureDisplay(5));
    }

    [Fact]
    public void CaptureWindow_UsesCompositeImage_FallsBackToRootRect()
    {
        var (svc, api) = Make(Disp(0x10, "eDP-1", 0, 0, 500, 500, primary: true));
        api.WindowInfos[(nint)0x42] = new XWindowInfo((nint)0x42, "Firefox", "firefox", "Navigator",
            20, 30, 300, 200, IsMapped: true, IsHidden: false);

        using var img = svc.CaptureWindow((nint)0x42);

        Assert.Equal(300, img.Width);
        Assert.Equal(200, img.Height);
        Assert.Equal(CaptureSource.Window, img.Source);
        var px = img.Bitmap.GetPixel(150, 100);
        Assert.Equal(0x33, px.Red);
        Assert.Equal(0x66, px.Green);
        Assert.Equal(0xCC, px.Blue);
    }

    [Fact]
    public void CaptureWindow_GoneWindow_Throws()
    {
        var (svc, _) = Make(Disp(0x10, "eDP-1", 0, 0, 500, 500, primary: true));
        Assert.Throws<InvalidOperationException>(() => svc.CaptureWindow((nint)0xdead));
    }
}
