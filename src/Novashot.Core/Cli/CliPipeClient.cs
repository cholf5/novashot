using System.IO.Pipes;

namespace Novashot.Core.Cli;

/// <summary>转发结果：Connected=false = 无实例（调用方回退 headless）；Connected=true 时以响应为准。</summary>
public sealed record CliForwardResult(bool Connected, int? ExitCode, string? Message, string? TransportError)
{
    public static readonly CliForwardResult NoInstance = new(false, null, null, null);

    public static CliForwardResult From(CliPipeResponse response) =>
        new(true, response.ExitCode, response.Message, null);
}

/// <summary>
/// L-1 管道客户端：廉价存在性探测 → 短超时连接既有实例 → 转发整条原始 argv → 取回执行结果（CLI 进程据此定退出码）。
/// 连接失败（无实例）→ <see cref="CliForwardResult.NoInstance"/>；已连接后传输失败不回退 headless
/// （命令可能已被服务端受理，本地重跑会出双份截图），按失败退出码 1 上报。
/// </summary>
public static class CliPipeClient
{
    /// <summary>实例连接超时（ms）。命中闸门后 connect 要么立即成功要么超时，500ms 足够覆盖调度抖动。</summary>
    public const int DefaultConnectTimeoutMs = 500;

    /// <summary>
    /// gui/无参 模式的连接超时（ms，review I-1）：GUI 冷启动不得为单实例探测固定付费——
    /// 廉价闸门无实例即 0 等待；闸门命中但实例已死（Unix stale socket 残留）时以此短超时兜底，冷启动上限 ~0.1s。
    /// </summary>
    public const int GuiConnectTimeoutMs = 100;

    /// <summary>响应等待超时（ms）。服务端串行执行抓帧+导出+--delay（上限 60s），给足余量。</summary>
    public const int DefaultResponseTimeoutMs = 120_000;

    /// <summary>
    /// 无实例廉价探测（review I-1）：先于 <see cref="NamedPipeClientStream.ConnectAsync"/> 的存在性检查——
    /// 无监听者时直接跳过转发尝试，省掉连接超时全额等待（gui/无参冷启动也走这里，
    /// 不能为单实例探测固定付出 ≈500ms，BASE 的 GUI 是即时启动）。
    /// - Windows：<c>File.Exists(@"\\.\pipe\{name}")</c>，命名管道对象存在即有服务端；
    /// - Unix：dotnet 把命名管道落成 <c>GetTempPath() + "CoreFxPipe_" + name</c> 的域 socket 文件，
    ///   文件不存在必无监听者。该路径格式是 .NET 运行时实现细节（Core 系长期稳定）：
    ///   若未来变更只会退化成「闸门恒 false → 永远跳过转发走 headless」，功能不炸只是慢，handoff 已记。
    /// 误报方向（无实例判有）→ 走一次注定快速失败的 ConnectAsync（Unix stale socket 残留 connect 秒拒
    /// ECONNREFUSED/ENOTSOCK），不产生双份截图；漏报方向 → 跳过转发直接 headless，同样安全。
    /// </summary>
    public static bool CheapProbe(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        try
        {
            return OperatingSystem.IsWindows()
                ? File.Exists($@"\\.\pipe\{pipeName}")
                : File.Exists(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName));
        }
        catch
        {
            return true; // 探测本身异常：保守放行，交给 ConnectAsync 的真实判定
        }
    }

    public static async Task<CliForwardResult> TryForwardAsync(
        string pipeName,
        string[] args,
        int connectTimeoutMs = DefaultConnectTimeoutMs,
        int responseTimeoutMs = DefaultResponseTimeoutMs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pipeName);
        ArgumentNullException.ThrowIfNull(args);

        // review I-1：闸门不命中（无实例）→ 不进 ConnectAsync，0 等待回退 headless
        if (!CheapProbe(pipeName))
            return CliForwardResult.NoInstance;

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            try
            {
                await pipe.ConnectAsync(connectTimeoutMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // TimeoutException / IOException / UnauthorizedAccessException / FileNotFoundException …
                // 一律视为「无实例」，由调用方回退 headless 执行
                return CliForwardResult.NoInstance;
            }

            var request = CliPipeProtocol.EncodeRequest(args);
            await pipe.WriteAsync(request, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);

            byte[]? payload;
            try
            {
                using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                responseCts.CancelAfter(responseTimeoutMs);
                payload = await CliPipeFrame.ReadAsync(pipe, responseCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new CliForwardResult(true, CliExitCodes.Failure, null,
                    $"Timed out after {responseTimeoutMs} ms waiting for the running instance.");
            }

            if (payload is null)
                return new CliForwardResult(true, CliExitCodes.Failure, null,
                    "The running instance closed the connection without a response.");

            if (!CliPipeProtocol.TryDecodeResponse(payload, out var response, out var error))
                return new CliForwardResult(true, CliExitCodes.Failure, null, $"Malformed response: {error}");

            return CliForwardResult.From(response!);
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
