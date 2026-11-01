using Prtsc.Core.Capture;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.Platform.Linux.Interop;
using Prtsc.Platform.Linux.Windowing;

namespace Prtsc.Linux.Tests;

/// <summary>
/// EWMH 窗口枚举过滤语义：未映射/隐藏/超小/自身剔除，标题回退 WM_CLASS，Z 序保持。
/// </summary>
public class LinuxWindowServiceTests
{
    private static XWindowInfo Win(nint h, string title = "Doc — Editor", string cls = "editor",
        int w = 800, int h2 = 600, bool mapped = true, bool hidden = false, string instance = "editor") =>
        new(h, title, cls, instance, 10, 20, w, h2, mapped, hidden);

    [Fact]
    public void EnumerateWindows_FiltersUnmappedHiddenTinySelf()
    {
        var api = new FakeXLibApi
        {
            Clients = [(nint)0x1, (nint)0x2, (nint)0x3, (nint)0x4, (nint)0x5, (nint)0x6],
        };
        api.WindowInfos[(nint)0x1] = Win(0x1, title: "Visible");
        api.WindowInfos[(nint)0x2] = Win(0x2, mapped: false);            // unmapped
        api.WindowInfos[(nint)0x3] = Win(0x3, hidden: true);             // minimized
        api.WindowInfos[(nint)0x4] = Win(0x4, w: 30, h2: 20);            // tiny
        api.WindowInfos[(nint)0x5] = Win(0x5, instance: "prtsc", cls: "prtsc"); // 自身
        api.WindowInfos[(nint)0x6] = Win(0x6, title: "", cls: "term");   // 无标题 → 回退 class

        var svc = new LinuxWindowService(api);
        var windows = svc.EnumerateWindows();

        Assert.Equal(2, windows.Count);
        Assert.Equal("Visible", windows[0].Title);
        Assert.Equal("term", windows[1].Title);
    }

    [Fact]
    public void GetForegroundWindow_ReturnsActiveMappedWindow()
    {
        var api = new FakeXLibApi();
        api.WindowInfos[(nint)0x10] = Win(0x10, title: "Front");
        Assert.NotNull(new LinuxWindowService(api).GetForegroundWindow());

        api.WindowInfos.Clear();
        api.WindowInfos[(nint)0x11] = Win(0x11, mapped: false);
        Assert.Null(new LinuxWindowService(api).GetForegroundWindow());
    }

    [Fact]
    public void IsSelf_MatchesInstanceOrClass()
    {
        Assert.True(LinuxWindowService.IsSelf(Win(0x1, instance: "prtsc", cls: "Prtsc")));
        Assert.True(LinuxWindowService.IsSelf(Win(0x1, instance: "editor", cls: "prtsc")));
        Assert.False(LinuxWindowService.IsSelf(Win(0x1, instance: "firefox", cls: "Navigator")));
    }

    [Fact]
    public void EnumerateWindows_KeepsZOrder()
    {
        var api = new FakeXLibApi { Clients = [(nint)0x9, (nint)0x8] };
        api.WindowInfos[(nint)0x9] = Win(0x9, title: "Topmost");
        api.WindowInfos[(nint)0x8] = Win(0x8, title: "Below");

        var windows = new LinuxWindowService(api).EnumerateWindows();
        Assert.Equal("Topmost", windows[0].Title);
    }
}
