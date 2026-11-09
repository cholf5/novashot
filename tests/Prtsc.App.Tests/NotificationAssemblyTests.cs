using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.Core;
using Prtsc.Core.Notifications;

#if WIN_PLATFORM
using Prtsc.Platform.Windows;
using Prtsc.Platform.Windows.Notifications;
#else
using Prtsc.Platform.Mac;
#endif

namespace Prtsc.App.Tests;

/// <summary>
/// N-1 通知装配矩阵（AppServices 实际装配顺序：先 Core 后平台，TFM 分腿编译）：
/// - windows TFM：AddPrtscCore（默认 Null）→ AddPrtscWindows 必须覆盖成 WinRT Toast；
/// - net10.0（mac/linux 腿）：必须保持 Core 默认的 Null 静默实现。
/// 只解析实例不调 Show——ToastNotificationService 的 ctor 无 WinRT 调用，mac 交叉编译
/// 运行安全；Show 的真机验证进 QA-1（见 handoff 诚实清单）。
/// </summary>
public class NotificationAssemblyTests
{
#if WIN_PLATFORM
    [Fact]
    public void AddPrtscCoreThenWindows_NotificationService_IsWinRtToast()
    {
        var services = new ServiceCollection();
        services.AddPrtscCore();
        services.AddPrtscWindows();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        var service = sp.GetRequiredService<INotificationService>();
        Assert.IsType<ToastNotificationService>(service);
        Assert.Equal("Prtsc", ToastNotificationService.AppUserModelId);
    }
#else
    [Fact]
    public void AddPrtscCoreThenMac_NotificationService_IsNullSilent()
    {
        var services = new ServiceCollection();
        services.AddPrtscCore();
        services.AddPrtscMac();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<NullNotificationService>(sp.GetRequiredService<INotificationService>());
    }
#endif
}
