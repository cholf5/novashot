using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Autostart;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Hotkeys;
using Prtsc.Core.Windows;
using Prtsc.Platform.Linux.Autostart;
using Prtsc.Platform.Linux.Capture;
using Prtsc.Platform.Linux.Clipboard;
using Prtsc.Platform.Linux.Hotkeys;
using Prtsc.Platform.Linux.Interop;
using Prtsc.Platform.Linux.Windowing;

namespace Prtsc.Platform.Linux;

/// <summary>
/// Linux 平台服务装配。全部 Core 接口的 X11 实现：
/// 捕获 XGetImage / 窗口 EWMH / 热键 XGrabKey / 剪贴板 xclip / 自启 XDG Autostart。
/// 仅在 OperatingSystem.IsLinux() 时调用（AppServices 运行时分流）。
/// </summary>
public static class LinuxServiceCollectionExtensions
{
    public static IServiceCollection AddPrtscLinux(this IServiceCollection services)
    {
        services.TryAddSingleton<IXLibApi, XLibApi>();
        services.TryAddSingleton<ICaptureService, LinuxCaptureService>();

        services.TryAddSingleton<IWindowService, LinuxWindowService>();

        services.TryAddSingleton<IHotkeyService, LinuxHotkeyService>();
        services.TryAddSingleton<IAutostartService, XdgAutostartService>();
        services.TryAddSingleton<IClipboardService, LinuxClipboardService>();
        return services;
    }
}
