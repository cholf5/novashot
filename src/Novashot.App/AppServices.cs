using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Novashot.App.ViewModels;
using Novashot.Core;
using Novashot.Core.Cloud;
using Novashot.Core.Clipboard;
using Novashot.Core.Config;
using Serilog;
using Serilog.Extensions.Logging;

#if WIN_PLATFORM
using Novashot.Platform.Windows;
#elif MAC_PLATFORM
using Novashot.Platform.Mac;
using Novashot.Platform.Linux;
#endif

namespace Novashot.App;

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

        // Core + platform（运行时按 OS 分流：windows TFM → Windows；net10.0 上 mac/linux 按 OperatingSystem 选）
        services.AddNovashotCore();
        services.AddNovashotPlatform();

        // 云上传：按 config.Cloud.Provider 每次分发（M7a 数据层；UI 在 M7b 接线）
        services.AddSingleton<IUploadService, ConfigUploadService>();
        services.AddSingleton<Novashot.Core.Library.LibraryStore>(sp =>
            new Novashot.Core.Library.LibraryStore(
                sp.GetRequiredService<ILogger<Novashot.Core.Library.LibraryStore>>()));

        // Override config directory if provided（测试或便携模式）
        services.AddSingleton<IConfigStore>(sp => new JsonConfigStore(
            sp.GetRequiredService<ILogger<JsonConfigStore>>(),
            overrideDirectory: configDirectory));

        // E-1 M9：会话级取色历史（overlay 写入 + StylePanel 历史行消费；非 config 持久化）
        services.AddSingleton<Novashot.Core.Editor.ColorHistory>();

        // ViewModels（IClipboardService 由 Platform 层注册；无 Platform 时退回进程内 Fake）
        services.TryAddSingleton<IClipboardService, FakeClipboardService>();
        services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IConfigStore>(),
            sp.GetService<IUploadService>(),
            sp.GetService<Novashot.Core.Library.LibraryStore>(),
            sp.GetService<ILogger<MainWindowViewModel>>(),
            sp.GetService<Novashot.Core.Editor.ColorHistory>(),
            sp.GetService<Novashot.Core.Notifications.INotificationService>()));

        _provider = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>
    /// 平台层装配桥：windows TFM → Platform.Windows；
    /// net10.0 TFM 上 mac 与 Linux 共用，按运行时 OS 分流（两个平台层都在包内，但只实例化命中的那套）。
    /// </summary>
    private static IServiceCollection AddNovashotPlatform(this IServiceCollection services)
#if WIN_PLATFORM
        => services.AddNovashotWindows();
#else
        => OperatingSystem.IsMacOS() ? services.AddNovashotMac()
        : OperatingSystem.IsLinux() ? services.AddNovashotLinux()
        : services.AddNovashotMac(); // 未知 Unix 类平台退回 mac 装配（让服务缺失在取用时早爆）
#endif

    public static void Shutdown()
    {
        _provider?.Dispose();
        _provider = null;
    }
}
