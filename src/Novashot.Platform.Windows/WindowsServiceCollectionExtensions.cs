using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Novashot.Core.Autostart;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Hotkeys;
using Novashot.Core.Notifications;
using Novashot.Core.Windows;
using Novashot.Platform.Windows.Autostart;
using Novashot.Platform.Windows.Capture;
using Novashot.Platform.Windows.Clipboard;
using Novashot.Platform.Windows.Hotkeys;
using Novashot.Platform.Windows.Notifications;
using Novashot.Platform.Windows.Windowing;

namespace Novashot.Platform.Windows;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddNovashotWindows(this IServiceCollection services)
    {
        // GdiCaptureService 作为 WGC 的回退与非 Window 路径实现，注册为具体类型
        services.TryAddSingleton<GdiCaptureService>();
        // WGC 主服务：CaptureWindow 走 WGC，其它委托 GDI
        services.TryAddSingleton<ICaptureService, WgcCaptureService>();

        services.TryAddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.TryAddSingleton<IAutostartService, RegistryAutostartService>();
        services.TryAddSingleton<IWindowService, WindowEnumerator>();
        services.TryAddSingleton<IClipboardService, WindowsClipboardService>();

        // N-1：Core 已默认注册 Null → 移除后换 WinRT Toast（未打包 AUMID 未注册时服务内自吞降级为日志）
        services.RemoveAll<INotificationService>();
        services.TryAddSingleton<INotificationService, ToastNotificationService>();
        return services;
    }
}
