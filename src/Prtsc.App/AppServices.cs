using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Prtsc.App.ViewModels;
using Prtsc.Core;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Platform.Windows;
using Serilog;
using Serilog.Extensions.Logging;

namespace Prtsc.App;

/// <summary>
/// 全局 DI 容器持有者。整个应用生命期只构建一次；View 层通过静态 <see cref="Services"/> 拿实例，
/// 或在 View 构造时通过 <see cref="GetRequiredService{T}"/> 显式获取。
/// </summary>
public static class AppServices
{
    private static ServiceProvider? _provider;

    public static IServiceProvider Services => _provider
        ?? throw new InvalidOperationException("AppServices not initialized");

    public static void Initialize(string configDirectory)
    {
        var services = new ServiceCollection();

        // Logging: Serilog 已通过 Log.Logger 静态配置，此处桥接到 Microsoft.Extensions.Logging。
        services.AddSingleton<ILoggerFactory>(_ => new SerilogLoggerFactory(Log.Logger, dispose: false));
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Logger<>));

        // Core + platform
        services.AddPrtscCore();
        services.AddPrtscWindows();

        // Override config directory if provided（测试或便携模式）
        services.AddSingleton<IConfigStore>(sp => new JsonConfigStore(
            sp.GetRequiredService<ILogger<JsonConfigStore>>(),
            overrideDirectory: configDirectory));

        // ViewModels（IClipboardService 由 Platform 层注册；无 Platform 时退回进程内 Fake）
        services.TryAddSingleton<IClipboardService, FakeClipboardService>();
        services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IConfigStore>(),
            sp.GetService<ILogger<MainWindowViewModel>>()));

        _provider = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static void Shutdown()
    {
        _provider?.Dispose();
        _provider = null;
    }
}
