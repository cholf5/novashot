using Prtsc.Platform.Mac.Capture;
using Prtsc.Platform.Mac.Windowing;
using Xunit;

namespace Prtsc.Mac.Tests;

/// <summary>
/// 真 CG interop 连通性（非 CI）：显示器枚举在本 shell 因屏幕录制 TCC 限制可能返回空，
/// 只验证调用不崩溃。像素级真捕获在真机 GUI 冒烟（QA）里验。
/// </summary>
[Trait("Category", "Manual")]
public class CGDisplayApiManualTests
{
    [Fact]
    public void EnumerateDisplays_DoesNotThrow()
    {
        var api = new CGDisplayApi();
        var displays = api.EnumerateDisplays();
        // 本 shell 可能 0 个（TCC）；GUI 会话下应 ≥1
        Assert.True(displays.Length >= 0);
    }

    [Fact]
    public void TryGetWindowBounds_AnyWindow_ReturnsOrReturnsFalse()
    {
        var api = new CGDisplayApi();
        // 不假设存在窗口；只验证调用路径不崩
        var ok = api.TryGetWindowBounds(1, out var bounds);
        Assert.True(ok || bounds.Size.Width == 0);
    }
}

[Trait("Category", "Manual")]
public class CGWindowListApiManualTests
{
    [Fact]
    public void CopyWindowList_DoesNotThrow()
    {
        var api = new CGWindowListApi();
        var rows = api.CopyWindowList();
        Assert.True(rows.Length >= 0);
    }
}

[Trait("Category", "Manual")]
public class MacHotkeyServiceManualTests
{
    [Fact]
    public void RegisterAndUnregister_F13_NoError()
    {
        using var svc = new Prtsc.Platform.Mac.Hotkeys.MacHotkeyService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Prtsc.Platform.Mac.Hotkeys.MacHotkeyService>.Instance);
        svc.Start();
        svc.RegisterAll("F13", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen");
        svc.Stop();
    }
}
