using System.IO.Pipes;

namespace Prtsc.Core.Cli;

/// <summary>转发结果：Connected=false = 无实例（调用方回退 headless）；Connected=true 时以响应为准。</summary>
public sealed record CliForwardResult(bool Connected, int? ExitCode, string? Message, string? TransportError)
{
    public static readonly CliForwardResult NoInstance = new(false, null, null, null);

    public static CliForwardResult From(CliPipeResponse response) =>
        new(true, response.ExitCode, response.Message, null);
}

/// <summary>
/// L-1 管道客户端：短超时探测既有实例 → 转发整条原始 argv → 取回执行结果（CLI 进程据此定退出码）。
/// 连接失败（无实例）→ <see cref="CliForwardResult.NoInstance"/>；已连接后传输失败不回退 headless
/// （命令可能已被服务端受理，本地重跑会出双份截图），按失败退出码 1 上报。
/// </summary>
public static class CliPipeClient
{
    /// <summary>实例探测超时（ms）。命名管道 connect 要么立即成功要么超时，500ms 足够覆盖调度抖动。</summary>
    public const int DefaultConnectTimeoutMs = 500;

    /// <summary>响应等待超时（ms）。服务端串行执行抓帧+导出+--delay（上限 60s），给足余量。</summary>
    public const int DefaultResponseTimeoutMs = 120_000;

    public static async Task<CliForwardResult> TryForwardAsync(
        string pipeName,
        string[] args,
        int connectTimeoutMs = DefaultConnectTimeoutMs,
        int responseTimeoutMs = DefaultResponseTimeoutMs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pipeName);
        ArgumentNullException.ThrowIfNull(args);

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
