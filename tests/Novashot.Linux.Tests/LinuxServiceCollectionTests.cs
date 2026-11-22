using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.Core;
using Novashot.Core.Autostart;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Hotkeys;
using Novashot.Core.Notifications;
using Novashot.Core.Windows;
using Novashot.Platform.Linux;
using Novashot.Platform.Linux.Autostart;
using Novashot.Platform.Linux.Capture;
using Novashot.Platform.Linux.Clipboard;
using Novashot.Platform.Linux.Hotkeys;
using Novashot.Platform.Linux.Interop;
using Novashot.Platform.Linux.Windowing;

namespace Novashot.Linux.Tests;

/// <summary>
/// DI 装配完整性：AddNovashotLinux 后全部 Core 平台接口可解析（对齐 MacServiceCollectionTests 范式）。
/// </summary>
public class LinuxServiceCollectionTests
{
    [Fact]
    public void AddNovashotLinux_RegistersAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddNovashotLinux();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<LinuxCaptureService>(sp.GetRequiredService<ICaptureService>());
        Assert.IsType<LinuxWindowService>(sp.GetRequiredService<IWindowService>());
        Assert.IsType<LinuxHotkeyService>(sp.GetRequiredService<IHotkeyService>());
        Assert.IsType<XdgAutostartService>(sp.GetRequiredService<IAutostartService>());
        Assert.IsType<LinuxClipboardService>(sp.GetRequiredService<IClipboardService>());
    }

    [Fact]
    public void AddNovashotLinux_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddNovashotLinux();
        services.AddNovashotLinux();
        using var sp = services.BuildServiceProvider();

        Assert.Same(sp.GetRequiredService<IXLibApi>(), sp.GetRequiredService<IXLibApi>());
    }

    /// <summary>N-1：linux 本卡不接原生通知——解析出的必须是 Null 静默实现（含 Core 先行的完整顺序）。</summary>
    [Fact]
    public void AddNovashotCoreThenLinux_NotificationService_IsNullSilent()
    {
        var services = new ServiceCollection();
        services.AddNovashotCore();
        services.AddNovashotLinux();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<NullNotificationService>(sp.GetRequiredService<INotificationService>());
    }
}
