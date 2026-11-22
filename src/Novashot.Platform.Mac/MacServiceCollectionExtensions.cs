using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Novashot.Core.Autostart;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Hotkeys;
using Novashot.Core.Notifications;
using Novashot.Core.Windows;
using Novashot.Platform.Mac.Autostart;
using Novashot.Platform.Mac.Capture;
using Novashot.Platform.Mac.Clipboard;
using Novashot.Platform.Mac.Hotkeys;
using Novashot.Platform.Mac.Windowing;

namespace Novashot.Platform.Mac;

/// <summary>
/// macOS 平台服务装配。全部 Core 接口的 mac 实现：
/// 捕获 CoreGraphics / 窗口 CGWindowList / 热键 Carbon / 剪贴板 NSPasteboard / 自启 launchd。
/// </summary>
public static class MacServiceCollectionExtensions
{
    public static IServiceCollection AddNovashotMac(this IServiceCollection services)
    {
        services.TryAddSingleton<IMacDisplayApi, CGDisplayApi>();
        services.TryAddSingleton<ICaptureService, MacCaptureService>();

        services.TryAddSingleton<IWindowListApi, CGWindowListApi>();
        services.TryAddSingleton<IWindowService>(sp =>
            new MacWindowService(sp.GetRequiredService<IWindowListApi>()));

        services.TryAddSingleton<IHotkeyService, MacHotkeyService>();
        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.TryAddSingleton<IAutostartService, LaunchdAutostartService>();
        services.TryAddSingleton<IClipboardService, MacClipboardService>();

        // N-1：mac 本卡不做原生通知（UNUserNotificationCenter 未排期，见 issue handoff）；
        // Core 已默认注册 Null，这里显式钉住装配意图（TryAdd 幂等）
        services.TryAddSingleton<INotificationService, NullNotificationService>();
        return services;
    }
}
