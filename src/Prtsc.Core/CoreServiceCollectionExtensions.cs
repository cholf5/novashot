using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prtsc.Core.Config;

namespace Prtsc.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPrtscCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IConfigStore, JsonConfigStore>();
        return services;
    }
}
