using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prtsc.Core.Capture;
using Prtsc.Core.Windows;
using Prtsc.Platform.Windows.Autostart;
using Prtsc.Platform.Windows.Capture;
using Prtsc.Platform.Windows.Hotkeys;
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
        return services;
    }
}
