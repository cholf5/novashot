using Novashot.App.Logging;
using Novashot.Core.Cli;
using Novashot.Core.Clipboard;
using Novashot.Core.Config;
using Novashot.Core.Capture;
using Serilog;

namespace Novashot.App.Cli;

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
            Console.Error.WriteLine($"novashot: {parse.Error}");
            Console.Error.WriteLine("Run 'novashot --help' for usage.");
            return CliRunResult.With(CliExitCodes.Usage);
        }

        // 不变式：Success 必带 Command（CliParseResult.Ok/Fail 成对构造）。这里收敛可空性：
        // 万一未来出现漏填 Command 的构造路径，退成可读错误而非 NRE。
        var command = parse.Command;
        if (command is null)
        {
            Console.Error.WriteLine("novashot: internal error: parsed command is missing.");
            return CliRunResult.With(CliExitCodes.Failure);
        }

        // 1) 单实例探测：已有实例 → 转发整条原始命令行，按响应退出（gui 在接收方映射为 activate）。
        //    review I-1：TryForwardAsync 内置廉价闸门（无实例 0 等待）；gui/无参 冷启动另用短连接超时，
        //    防 Unix stale socket 残留把探测拖满 500ms——GUI 冷启动上限 ~0.1s（BASE 即时启动）。
        var connectTimeoutMs = command.Mode == CliCaptureMode.Gui
            ? CliPipeClient.GuiConnectTimeoutMs
            : CliPipeClient.DefaultConnectTimeoutMs;
        CliForwardResult forward;
        try
        {
            forward = CliPipeClient.TryForwardAsync(CliPipeNames.ForCurrentUser(), args, connectTimeoutMs)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 终审必修③：连接成功后的 write/read IOException 等传输异常不再裸抛——
            // 记日志（gui/无参也已预配置 file-only logger）+ stderr 一行，统一转失败退出码
            Log.Error(ex, "CLI forwarding failed for mode {Mode}", command.Mode);
            Console.Error.WriteLine($"novashot: forwarding to the running instance failed: {ex.Message}");
            return CliRunResult.With(CliExitCodes.Failure);
        }
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
                    ?? "The running Novashot instance reported a failure.");
            }
            return CliRunResult.With(forward.ExitCode ?? CliExitCodes.Failure);
        }

        // 2) 无实例：gui → 回 Program 走现行 GUI 启动路径
        if (command.Mode == CliCaptureMode.Gui)
            return new CliRunResult(CliRunOutcome.LaunchGui, CliExitCodes.Success);

        // 3) 无实例的交互模式：headless 做不了，明确报错退出（不启动 GUI）
        if (command.IsInteractive)
        {
            if (command.Mode == CliCaptureMode.Region)
            {
                Console.Error.WriteLine(
                    "novashot: headless region capture requires --geometry X,Y,W,H (physical pixels, virtual-desktop coordinates).");
            }
            else
            {
                Console.Error.WriteLine(
                    "novashot: window capture needs interactive selection; start the GUI first ('novashot gui') and run this command again.");
            }
            Console.Error.WriteLine("Run 'novashot --help' for usage.");
            return CliRunResult.With(command.Mode == CliCaptureMode.Region
                ? CliExitCodes.Usage
                : CliExitCodes.Failure);
        }

        // 4) headless 执行（full / region+geometry）
        return CliRunResult.With(RunHeadless(command, configDir));
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
            Console.Error.WriteLine($"novashot: {result.Message}");
            return CliExitCodes.Failure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"novashot: {ex.Message}");
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
