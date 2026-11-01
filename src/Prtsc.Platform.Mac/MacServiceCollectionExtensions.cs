using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prtsc.Core.Clipboard;

namespace Prtsc.Platform.Mac;

/// <summary>
/// macOS 平台服务装配。逐步补齐：当前为骨架（剪贴板暂用 Core 的进程内 Fake 兜底）。
/// </summary>
public static class MacServiceCollectionExtensions
{
    public static IServiceCollection AddPrtscMac(this IServiceCollection services)
    {
        // 骨架阶段：尚未有平台剪贴板实现时退回进程内 Fake（语义 = TryAdd，被后续真实现覆盖）
        services.TryAddSingleton<IClipboardService, FakeClipboardService>();
        return services;
    }
}
