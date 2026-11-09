using System.IO.Pipes;
using Microsoft.Extensions.Logging;

namespace Prtsc.Core.Cli;

/// <summary>
/// L-1 管道服务端宿主：GUI 应用运行期间持有（App 启动时 Start、退出时 Dispose），全程吞异常——
/// 管道任何故障（建流失败/连接中断/取消）都不允许影响 GUI 主流程，最多记 Debug 日志。
/// 每连接一个 Task，一次一命令：解码 → CliArgs 解析（失败回 usage error）→ handler → 回写响应 → 关闭。
/// </summary>
public sealed class CliPipeServerHost : IDisposable
{
    /// <summary>同时可容纳的管道实例数：accept 循环在处理上一连接期间继续接收下一连接的余量。</summary>
    private const int MaxConcurrentPipeInstances = 4;

    /// <summary>accept 循环故障重试间隔（防名称冲突等异常把 CPU 打满）。</summary>
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly string _pipeName;
    private readonly Func<CliCommand, CancellationToken, Task<CliPipeResponse>> _handler;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private NamedPipeServerStream? _current;
    private bool _disposed;

    public CliPipeServerHost(string pipeName,
        Func<CliCommand, CancellationToken, Task<CliPipeResponse>> handler,
        ILogger? logger = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName))
            : pipeName;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger;
    }

    public string PipeName => _pipeName;

    /// <summary>启动 accept 循环（fire-and-forget）。幂等；任何异常都留在循环内，绝不抛给宿主。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop is not null) return;
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    MaxConcurrentPipeInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                _current = server;
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _current = null;
                // 一连接一 Task，一次一命令；流的生命周期归连接处理
                _ = Task.Run(() => HandleConnectionAsync(server, ct), ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _logger?.LogDebug(ex, "CliPipeServerHost accept loop retrying on {PipeName}", _pipeName);
                try
                {
                    await Task.Delay(AcceptRetryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using (stream)
            {
                var response = await HandleSingleCommandAsync(stream, ct).ConfigureAwait(false);
                var frame = CliPipeProtocol.EncodeResponse(response);
                await stream.WriteAsync(frame, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // 客户端提前断开 / 取消 / 写失败——连接级故障，吞掉不影响宿主
            _logger?.LogDebug(ex, "CliPipeServerHost connection ended abnormally");
        }
    }

    private async Task<CliPipeResponse> HandleSingleCommandAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        var payload = await CliPipeFrame.ReadAsync(stream, ct).ConfigureAwait(false);
        if (payload is null)
            return CliPipeResponse.Usage("Empty request.");

        if (!CliPipeProtocol.TryDecodeRequest(payload, out var args, out var error))
            return CliPipeResponse.Usage($"Invalid request: {error}");

        var parse = CliArgs.Parse(args);
        if (!parse.Success)
            return CliPipeResponse.Usage(parse.Error ?? "Invalid command.");

        try
        {
            return await _handler(parse.Command!, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 宿主退出：让连接处理走统一吞异常路径，不再回写响应
        }
        catch (Exception ex)
        {
            return CliPipeResponse.Fail(CliExitCodes.Failure, $"Command handling failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _current?.Dispose();
        }
        catch
        {
            // Dispose 阶段的任何故障无关紧要
        }
        _cts.Dispose();
    }
}
