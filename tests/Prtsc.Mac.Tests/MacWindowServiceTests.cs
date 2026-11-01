using Prtsc.Platform.Mac.Interop;
using Prtsc.Platform.Mac.Windowing;
using Xunit;

namespace Prtsc.Mac.Tests;

/// <summary>
/// CGWindowList 字典行 → WindowInfo 的解析与过滤语义。
/// WinListRow 是 interop 层产出的 DTO（供 Fake）。
/// </summary>
public class MacWindowServiceTests
{
    private static WinListRow Row(uint id, string owner, string title, double x, double y, double w, double h, int layer = 0, double alpha = 1.0, bool onscreen = true, int pid = 100) =>
        new(id, owner, title, new CGRect(x, y, w, h), layer, alpha, onscreen, pid);

    [Fact]
    public void Parse_NormalWindow_FullInfo()
    {
        var svc = new MacWindowService(new FakeWindowListApi([Row(42, "Safari", "GitHub · Build", 100, 200, 1200, 800)]), "Prtsc", 2.0);
        var wins = svc.EnumerateWindows();

        var w = Assert.Single(wins);
        Assert.Equal(42, w.Handle);
        Assert.Equal("GitHub · Build", w.Title);
        Assert.Equal("Safari", w.ProcessName);
        // 点 × 主屏 Scale = 物理
        Assert.Equal(200, w.Bounds.X);
        Assert.Equal(400, w.Bounds.Y);
        Assert.Equal(2400, w.Bounds.Width);
        Assert.Equal(1600, w.Bounds.Height);
        Assert.False(w.IsMinimized);
    }

    [Fact]
    public void Parse_MissingTitle_FallsBackToOwner()
    {
        var svc = new MacWindowService(new FakeWindowListApi([Row(7, "Finder", "", 0, 0, 800, 600)]), "Prtsc", 1.0);
        var wins = svc.EnumerateWindows();
        Assert.Equal("Finder", Assert.Single(wins).Title);
    }

    [Fact]
    public void Filter_NonNormalLayer_Excluded()
    {
        var api = new FakeWindowListApi([
            Row(1, "Window Server", "", 0, 0, 100, 100, layer: 0),
            Row(2, "Control Center", "", 0, 0, 100, 100, layer: 25),
            Row(3, "Dock", "", 0, 0, 100, 100, layer: 20),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        var wins = svc.EnumerateWindows();
        Assert.Equal([1], wins.Select(w => w.Handle));
    }

    [Fact]
    public void Filter_SelfProcess_Excluded()
    {
        var api = new FakeWindowListApi([
            Row(1, "OtherApp", "doc", 0, 0, 500, 500, pid: 100),
            Row(2, "Prtsc", "Prtsc", 0, 0, 500, 500, pid: 999),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        var wins = svc.EnumerateWindows();
        Assert.Equal([1], wins.Select(w => w.Handle));
    }

    [Fact]
    public void Filter_TinyOrTransparentWindows_Excluded()
    {
        var api = new FakeWindowListApi([
            Row(1, "App", "tiny", 0, 0, 5, 5),
            Row(2, "App", "invisible", 0, 0, 500, 500, alpha: 0.0),
            Row(3, "App", "ok", 0, 0, 500, 500),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        var wins = svc.EnumerateWindows();
        Assert.Equal([3], wins.Select(w => w.Handle));
    }

    [Fact]
    public void Filter_Offscreen_IsMinimizedButListed()
    {
        // CGWindowListOptionAll 包含离屏窗口：最小化窗口保留在列表里标 IsMinimized
        var api = new FakeWindowListApi([
            Row(1, "App", "front", 0, 0, 500, 500, onscreen: true),
            Row(2, "App", "minimized", 0, 0, 500, 500, onscreen: false),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        var wins = svc.EnumerateWindows();
        Assert.Equal(2, wins.Count);
        Assert.False(wins[0].IsMinimized);
        Assert.True(wins[1].IsMinimized);
    }

    [Fact]
    public void ZOrder_PreservedFromArrayOrder()
    {
        var api = new FakeWindowListApi([
            Row(9, "Front", "a", 0, 0, 100, 100),
            Row(8, "Mid", "b", 0, 0, 100, 100),
            Row(7, "Back", "c", 0, 0, 100, 100),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        Assert.Equal([9, 8, 7], svc.EnumerateWindows().Select(w => w.Handle));
    }

    [Fact]
    public void GetForegroundWindow_ReturnsFirstOnscreenNonSelf()
    {
        var api = new FakeWindowListApi([
            Row(1, "App", "on", 0, 0, 100, 100, onscreen: true),
            Row(2, "App", "off", 0, 0, 100, 100, onscreen: false),
        ]);
        var svc = new MacWindowService(api, "Prtsc", 1.0);
        Assert.Equal(1, svc.GetForegroundWindow()?.Handle);

        var empty = new MacWindowService(new FakeWindowListApi([]), "Prtsc", 1.0);
        Assert.Null(empty.GetForegroundWindow());
    }
}
