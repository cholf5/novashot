using Avalonia;
using Prtsc.App.Logging;
using Prtsc.Core.Config;
using Serilog;
using Velopack;

namespace Prtsc.App;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // R-1：Velopack 入口钩子必须在 Main 最早处（vpk 安装器/更新器的 --velopack-* 参数在这里被消化）。
        // 未打包环境（开发跑 / zip 便携）该调用安全空转：参数不匹配任何钩子，同时把默认 locator 装配好
        // 供后续 UpdateManager.IsInstalled 判断使用。
        VelopackApp.Build().Run();

        var configDir = JsonConfigStore.DefaultConfigDirectory();
        Directory.CreateDirectory(configDir);

        LoggingSetup.Configure(configDir);

        try
        {
            AppServices.Initialize(configDir);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            LoggingSetup.Close();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
