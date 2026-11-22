using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Novashot.Core.Autostart;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Hotkeys;
using Novashot.Core.Notifications;
using Novashot.Core.Windows;
using Novashot.Platform.Linux.Autostart;
using Novashot.Platform.Linux.Capture;
using Novashot.Platform.Linux.Clipboard;
using Novashot.Platform.Linux.Hotkeys;
using Novashot.Platform.Linux.Interop;
using Novashot.Platform.Linux.Windowing;

namespace Novashot.Platform.Linux;

/// <summary>
/// Linux 平台服务装配。全部 Core 接口的 X11 实现：
/// 捕获 XGetImage / 窗口 EWMH / 热键 XGrabKey / 剪贴板 xclip / 自启 XDG Autostart。
/// 仅在 OperatingSystem.IsLinux() 时调用（AppServices 运行时分流）。
/// </summary>
public static class LinuxServiceCollectionExtensions
{
    public static IServiceCollection AddNovashotLinux(this IServiceCollection services)
    {
        services.TryAddSingleton<IXLibApi, XLibApi>();
        services.TryAddSingleton<ICaptureService, LinuxCaptureService>();

        services.TryAddSingleton<IWindowService, LinuxWindowService>();

        services.TryAddSingleton<IHotkeyService, LinuxHotkeyService>();
        services.TryAddSingleton<IAutostartService, XdgAutostartService>();
        services.TryAddSingleton<IClipboardService, LinuxClipboardService>();

        // N-1：linux 本卡不做原生通知（AppIndicator/libnotify 未排期，见 issue handoff）；
        // Core 已默认注册 Null，这里显式钉住装配意图（TryAdd 幂等）
        services.TryAddSingleton<INotificationService, NullNotificationService>();
        return services;
    }
}
