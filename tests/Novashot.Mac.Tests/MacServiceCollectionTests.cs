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
using Novashot.Platform.Mac;
using Novashot.Platform.Mac.Autostart;
using Novashot.Platform.Mac.Capture;
using Novashot.Platform.Mac.Clipboard;
using Novashot.Platform.Mac.Hotkeys;
using Novashot.Platform.Mac.Windowing;

namespace Novashot.Mac.Tests;

/// <summary>
/// DI 装配冒烟：AddNovashotMac 后所有 Core 平台接口都能解析出 mac 实现。
/// 不启动 GUI（遵守"不主动运行 Novashot"约定）。
/// </summary>
public class MacServiceCollectionTests
{
    [Fact]
    public void AddNovashotMac_ResolvesAllCorePlatformInterfaces()
    {
        var services = new ServiceCollection();
        services.AddNovashotMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        using var sp = services.BuildServiceProvider();

        var capture = sp.GetRequiredService<ICaptureService>();
        Assert.IsType<MacCaptureService>(capture);

        Assert.IsType<MacWindowService>(sp.GetRequiredService<IWindowService>());
        Assert.IsType<MacHotkeyService>(sp.GetRequiredService<IHotkeyService>());
        Assert.IsType<LaunchdAutostartService>(sp.GetRequiredService<IAutostartService>());
        Assert.IsType<MacClipboardService>(sp.GetRequiredService<IClipboardService>());
    }

    [Fact]
    public void AddNovashotMac_CaptureServiceUsesRealCGApi()
    {
        var services = new ServiceCollection();
        services.AddNovashotMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        var api = sp.GetRequiredService<IMacDisplayApi>();
        Assert.IsType<CGDisplayApi>(api);
    }

    /// <summary>N-1：mac 本卡不接原生通知——解析出的必须是 Null 静默实现。</summary>
    [Fact]
    public void AddNovashotMac_NotificationService_IsNullSilent()
    {
        var services = new ServiceCollection();
        services.AddNovashotMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<NullNotificationService>(sp.GetRequiredService<INotificationService>());
    }

    /// <summary>N-1：完整装配顺序（AppServices 先 Core 后平台）下通知仍是 Null——Core 默认不被覆盖。</summary>
    [Fact]
    public void AddNovashotCoreThenMac_NotificationService_StillNullSilent()
    {
        var services = new ServiceCollection();
        services.AddNovashotCore();
        services.AddNovashotMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<NullNotificationService>(sp.GetRequiredService<INotificationService>());
    }
}
