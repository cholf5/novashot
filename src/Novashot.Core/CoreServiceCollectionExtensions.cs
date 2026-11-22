using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Novashot.Core.Config;
using Novashot.Core.Notifications;

namespace Novashot.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddNovashotCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IConfigStore, JsonConfigStore>();
        // N-1：桌面通知默认 Null（静默）；Windows 装配时移除后换成 WinRT Toast
        services.TryAddSingleton<INotificationService, NullNotificationService>();
        return services;
    }
}
