using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.Core.Autostart;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Hotkeys;
using Prtsc.Core.Windows;
using Prtsc.Platform.Mac;
using Prtsc.Platform.Mac.Autostart;
using Prtsc.Platform.Mac.Capture;
using Prtsc.Platform.Mac.Clipboard;
using Prtsc.Platform.Mac.Hotkeys;
using Prtsc.Platform.Mac.Windowing;

namespace Prtsc.Mac.Tests;

/// <summary>
/// DI 装配冒烟：AddPrtscMac 后所有 Core 平台接口都能解析出 mac 实现。
/// 不启动 GUI（遵守"不主动运行 Prtsc"约定）。
/// </summary>
public class MacServiceCollectionTests
{
    [Fact]
    public void AddPrtscMac_ResolvesAllCorePlatformInterfaces()
    {
        var services = new ServiceCollection();
        services.AddPrtscMac();
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
    public void AddPrtscMac_CaptureServiceUsesRealCGApi()
    {
        var services = new ServiceCollection();
        services.AddPrtscMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        var api = sp.GetRequiredService<IMacDisplayApi>();
        Assert.IsType<CGDisplayApi>(api);
    }
}
