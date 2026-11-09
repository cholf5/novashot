using Avalonia;
using Prtsc.App.Cli;
using Prtsc.App.Logging;
using Prtsc.Core.Cli;
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

        try
        {
            // L-1 CLI 分流（关键约束：捕获命令绝不进入 Avalonia 运行时）：
            // 纯函数解析 → CLI 命令走 CliApplication（转发/headless）后直接返回退出码；
            // gui / 无参数走现行 GUI 路径。
            // 日志形态（终审必修③）：所有进程一律先 file-only——stdout/stderr 是脚本契约，
            // 且 gui 转发路径的意外异常必须落日志（此前 gui 不预配置 → Log.Fatal 落 SilentLogger，
            // exit 1 且 stderr/日志全空）；真启动 GUI 时再 re-Configure 回 console+file
            //（代价是 GUI 启动日志多一对 "Prtsc starting"，终审明确接受，压过 batch7 M-7 观感项）。
            var parse = CliArgs.Parse(args);
            LoggingSetup.Configure(configDir, consoleSink: false);

            var run = CliApplication.Run(parse, args, configDir);
            if (run.Outcome == CliRunOutcome.LaunchGui)
            {
                LoggingSetup.Configure(configDir, consoleSink: true);
                AppServices.Initialize(configDir);
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            return run.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            Console.Error.WriteLine($"prtsc: {ex.Message}"); // 终审必修③兜底：日志不可用/被吞时 stderr 至少有一行
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
