using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.Core;
using Prtsc.Core.Autostart;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Hotkeys;
using Prtsc.Core.Notifications;
using Prtsc.Core.Windows;
using Prtsc.Platform.Linux;
using Prtsc.Platform.Linux.Autostart;
using Prtsc.Platform.Linux.Capture;
using Prtsc.Platform.Linux.Clipboard;
using Prtsc.Platform.Linux.Hotkeys;
using Prtsc.Platform.Linux.Interop;
using Prtsc.Platform.Linux.Windowing;

namespace Prtsc.Linux.Tests;

/// <summary>
/// DI 装配完整性：AddPrtscLinux 后全部 Core 平台接口可解析（对齐 MacServiceCollectionTests 范式）。
/// </summary>
public class LinuxServiceCollectionTests
{
    [Fact]
    public void AddPrtscLinux_RegistersAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddPrtscLinux();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<LinuxCaptureService>(sp.GetRequiredService<ICaptureService>());
        Assert.IsType<LinuxWindowService>(sp.GetRequiredService<IWindowService>());
        Assert.IsType<LinuxHotkeyService>(sp.GetRequiredService<IHotkeyService>());
        Assert.IsType<XdgAutostartService>(sp.GetRequiredService<IAutostartService>());
        Assert.IsType<LinuxClipboardService>(sp.GetRequiredService<IClipboardService>());
    }

    [Fact]
    public void AddPrtscLinux_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddPrtscLinux();
        services.AddPrtscLinux();
        using var sp = services.BuildServiceProvider();

        Assert.Same(sp.GetRequiredService<IXLibApi>(), sp.GetRequiredService<IXLibApi>());
    }

    /// <summary>N-1：linux 本卡不接原生通知——解析出的必须是 Null 静默实现（含 Core 先行的完整顺序）。</summary>
    [Fact]
    public void AddPrtscCoreThenLinux_NotificationService_IsNullSilent()
    {
        var services = new ServiceCollection();
        services.AddPrtscCore();
        services.AddPrtscLinux();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<NullNotificationService>(sp.GetRequiredService<INotificationService>());
    }
}
