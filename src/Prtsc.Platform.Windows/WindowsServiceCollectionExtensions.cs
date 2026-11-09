using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prtsc.Core.Autostart;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Hotkeys;
using Prtsc.Core.Notifications;
using Prtsc.Core.Windows;
using Prtsc.Platform.Windows.Autostart;
using Prtsc.Platform.Windows.Capture;
using Prtsc.Platform.Windows.Clipboard;
using Prtsc.Platform.Windows.Hotkeys;
using Prtsc.Platform.Windows.Notifications;
using Prtsc.Platform.Windows.Windowing;

namespace Prtsc.Platform.Windows;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddPrtscWindows(this IServiceCollection services)
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
