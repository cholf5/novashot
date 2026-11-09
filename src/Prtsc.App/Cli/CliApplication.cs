using Prtsc.App.Logging;
using Prtsc.Core.Cli;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Capture;
using Serilog;

namespace Prtsc.App.Cli;

/// <summary>CLI 进程的一次运行结局：Exit = 按退出码退出；LaunchGui = 无实例且模式为 gui，回 Program 走现行 GUI 启动。</summary>
public enum CliRunOutcome
{
    Exit,
    LaunchGui,
}

public sealed record CliRunResult(CliRunOutcome Outcome, int ExitCode)
{
    public static CliRunResult With(int exitCode) => new(CliRunOutcome.Exit, exitCode);
}

/// <summary>
/// L-1 CLI 进程入口编排（Program.Main 分流调用；在 <c>BuildAvaloniaApp</c> 之前、绝不触碰 Avalonia 运行时）：
/// --help 本地打印即退 → 单实例探测（有实例转发整条 argv 后退出）→ 无实例按模式分流
/// （gui → LaunchGui 回 GUI 路径；region 缺 geometry → usage error；window → 报错不启动 GUI；
/// full / region+geometry → headless 执行）。退出码约定：0 成功 / 1 一般失败 / 2 用法错误。
/// 输出语言：CLI 全程 en，不经 Loc 表（PM 裁决，见 handoff）。
/// </summary>
public static class CliApplication
{
    /// <summary>parse 已由 Program.Main 完成（决定日志 sink 形态），这里只做编排。</summary>
    public static CliRunResult Run(CliParseResult parse, string[] args, string configDir)
    {
        if (parse.Command is { Help: true })
        {
            Console.Write(CliArgs.HelpText);
            return CliRunResult.With(CliExitCodes.Success);
        }

        if (!parse.Success)
        {
            Console.Error.WriteLine($"prtsc: {parse.Error}");
            Console.Error.WriteLine("Run 'prtsc --help' for usage.");
            return CliRunResult.With(CliExitCodes.Usage);
        }

        // 1) 单实例探测：已有实例 → 转发整条原始命令行，按响应退出（gui 在接收方映射为 activate）。
        //    review I-1：TryForwardAsync 内置廉价闸门（无实例 0 等待）；gui/无参 冷启动另用短连接超时，
        //    防 Unix stale socket 残留把探测拖满 500ms——GUI 冷启动上限 ~0.1s（BASE 即时启动）。
        var connectTimeoutMs = parse.Command.Mode == CliCaptureMode.Gui
            ? CliPipeClient.GuiConnectTimeoutMs
            : CliPipeClient.DefaultConnectTimeoutMs;
        var forward = CliPipeClient.TryForwardAsync(CliPipeNames.ForCurrentUser(), args, connectTimeoutMs)
            .GetAwaiter().GetResult();
        if (forward.Connected)
        {
            if (forward.ExitCode == CliExitCodes.Success)
            {
                if (!string.IsNullOrEmpty(forward.Message))
                    Console.WriteLine(forward.Message);
            }
            else
            {
                Console.Error.WriteLine(forward.Message
                    ?? forward.TransportError
                    ?? "The running Prtsc instance reported a failure.");
            }
            return CliRunResult.With(forward.ExitCode ?? CliExitCodes.Failure);
        }

        // 2) 无实例：gui → 回 Program 走现行 GUI 启动路径
        if (parse.Command.Mode == CliCaptureMode.Gui)
            return new CliRunResult(CliRunOutcome.LaunchGui, CliExitCodes.Success);

        // 3) 无实例的交互模式：headless 做不了，明确报错退出（不启动 GUI）
        if (parse.Command.IsInteractive)
        {
            if (parse.Command.Mode == CliCaptureMode.Region)
            {
                Console.Error.WriteLine(
                    "prtsc: headless region capture requires --geometry X,Y,W,H (physical pixels, virtual-desktop coordinates).");
            }
            else
            {
                Console.Error.WriteLine(
                    "prtsc: window capture needs interactive selection; start the GUI first ('prtsc gui') and run this command again.");
            }
            Console.Error.WriteLine("Run 'prtsc --help' for usage.");
            return CliRunResult.With(parse.Command.Mode == CliCaptureMode.Region
                ? CliExitCodes.Usage
                : CliExitCodes.Failure);
        }

        // 4) headless 执行（full / region+geometry）
        return CliRunResult.With(RunHeadless(parse.Command, configDir));
    }

    private static int RunHeadless(CliCommand command, string configDir)
    {
        ConsoleCancelEventHandler? cancelHandler = null;
        try
        {
            using var cts = new CancellationTokenSource();
            // Ctrl+C：第一次 = 请求取消，进程走正常退出路径拿退出码（e.Cancel=true 吞掉本次 SIGINT）；
            // 已取消后再按（原生抓帧/导出悬挂时）= 放行默认终止，保证进程杀得掉（review M-6）。
            cancelHandler = (_, e) =>
            {
                if (cts.IsCancellationRequested)
                {
                    e.Cancel = false;
                    return;
                }
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            AppServices.Initialize(configDir);
            var store = AppServices.GetRequiredService<IConfigStore>();
            Task.Run(() => store.LoadAsync()).GetAwaiter().GetResult();

            var executor = new CliCaptureExecutor(
                AppServices.GetRequiredService<ICaptureService>(),
                store,
                AppServices.GetRequiredService<IClipboardService>());

            var result = executor.ExecuteAsync(command, cts.Token).GetAwaiter().GetResult();
            if (result.Success)
            {
                Console.WriteLine(result.Message);
                return CliExitCodes.Success;
            }
            Console.Error.WriteLine($"prtsc: {result.Message}");
            return CliExitCodes.Failure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"prtsc: {ex.Message}");
            Log.Error(ex, "CLI headless execution failed for {Mode}", command.Mode);
            return CliExitCodes.Failure;
        }
        finally
        {
            if (cancelHandler is not null)
                Console.CancelKeyPress -= cancelHandler;
            AppServices.Shutdown();
            LoggingSetup.Close();
        }
    }
}
