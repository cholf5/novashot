using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prtsc.Core.Config;
using Prtsc.Core.Notifications;

namespace Prtsc.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPrtscCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IConfigStore, JsonConfigStore>();
        // N-1：桌面通知默认 Null（静默）；Windows 装配时移除后换成 WinRT Toast
        services.TryAddSingleton<INotificationService, NullNotificationService>();
        return services;
    }
}
