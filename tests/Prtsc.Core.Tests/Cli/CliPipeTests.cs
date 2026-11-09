using System.IO.Pipes;
using System.Text;
using Prtsc.Core.Cli;

namespace Prtsc.Core.Tests;

/// <summary>
/// L-1 管道端到端（真 NamedPipeServerStream/ClientStream，headless 可跑）：
/// 客户端转发 → 服务端解码/解析 → handler 执行 → 响应回传 → 客户端拿退出码。
/// 另盖无实例回退、坏帧容错、宿主 Start/Dispose 不炸、每用户管道名派生。
/// 坑（本测试首次踩出）：Unix 下 dotnet 把管道落成 GetTempPath() + "CoreFxPipe_" + name 的
/// domain socket，路径上限 104 字符——名字必须短（生产名 prtsc-cli-+8hex=18 字符，macOS 临时目录
/// ~49+11 前缀后总长 78，安全）；测试用 8 位随机 tag 而不是 GUID，避免并行撞名又超长。
/// </summary>
public class CliPipeTests
{
    private static string UniquePipeName() => $"prtsc-cli-t{Guid.NewGuid():N}".AsSpan(0, 17).ToString();

    // ---------- 客户端 × 服务端端到端 ----------

    [Fact]
    public async Task Forward_OkHandler_ReturnsExitCode0AndMessage()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName,
            (_, _) => Task.FromResult(CliPipeResponse.Success("Saved to /tmp/x.png (10 bytes)")));
        host.Start();

        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full", "--clipboard"], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Saved to /tmp/x.png (10 bytes)", result.Message);
        Assert.Null(result.TransportError);
    }

    [Fact]
    public async Task Forward_FailingHandler_ReturnsExitCode1()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName,
            (_, _) => Task.FromResult(CliPipeResponse.Fail(1, "Capture failed: no permission")));
        host.Start();

        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("no permission", result.Message);
    }

    [Fact]
    public async Task Forward_UsageErrorResponse_ReturnsExitCode2()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName,
            (_, _) => Task.FromResult(CliPipeResponse.Success("never called")));
        host.Start();

        // 服务端对无法解析的命令自己回 usage error，handler 不被调用
        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full", "--nope"], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown option", result.Message);
    }

    [Fact]
    public async Task Forward_HandlerThrows_ServiceRespondsGenericFailure()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName,
            (_, _) => throw new InvalidOperationException("di crashed"));
        host.Start();

        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("di crashed", result.Message);
    }

    [Fact]
    public async Task Forward_SequentialRequests_BothServed()
    {
        var pipeName = UniquePipeName();
        var handled = new List<string>();
        using var host = new CliPipeServerHost(pipeName,
            (cmd, _) =>
            {
                lock (handled) handled.Add(cmd.Mode.ToString());
                return Task.FromResult(CliPipeResponse.Success($"ok-{handled.Count}"));
            });
        host.Start();

        var first = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 2000);
        var second = await CliPipeClient.TryForwardAsync(pipeName, ["region"], connectTimeoutMs: 2000);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        lock (handled) Assert.Equal(["Full", "Region"], handled);
    }

    [Fact]
    public async Task Forward_RawClientWithoutHandler_ReceivesUsageError()
    {
        // 直接用裸管道写坏帧：服务端应回 usage error 而不是崩
        var pipeName = UniquePipeName();
        var served = new TaskCompletionSource();
        using var host = new CliPipeServerHost(pipeName,
            (_, _) => { served.TrySetResult(); return Task.FromResult(CliPipeResponse.Success("should not happen")); });
        host.Start();

        var response = await SendRawAndReadResponseAsync(pipeName, "garbage not json\n");

        Assert.False(response.Ok);
        Assert.Equal(2, response.ExitCode);
        Assert.False(served.Task.IsCompleted); // handler 未被调用
    }

    // ---------- 无实例回退 ----------

    [Fact]
    public async Task TryForward_NoServer_ReturnsNoInstance()
    {
        var pipeName = UniquePipeName(); // 没有服务端
        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 300);

        Assert.False(result.Connected);
        Assert.Null(result.ExitCode);
        Assert.Null(result.TransportError);
    }

    // ---------- 宿主装配 ----------

    [Fact]
    public async Task Host_StartDispose_NoConnection_NoThrow()
    {
        var pipeName = UniquePipeName();
        var host = new CliPipeServerHost(pipeName, (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));
        host.Start();
        host.Start(); // 幂等
        await Task.Delay(50);
        host.Dispose();
        host.Dispose(); // 幂等
    }

    [Fact]
    public async Task Host_DisposeWhileClientWaits_ClientGetsTransportFailure()
    {
        var pipeName = UniquePipeName();
        var host = new CliPipeServerHost(pipeName, (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));
        host.Start();
        await Task.Delay(50);
        host.Dispose();

        // 服务端已停：此时连接可能失败（无实例回退）也可能连上但无响应（传输失败），都不得挂死
        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 1000, responseTimeoutMs: 3000);
        if (result.Connected)
            Assert.NotNull(result.TransportError);
        else
            Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Host_EmptyRequestLine_RespondsUsageError()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName, (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));
        host.Start();

        var response = await SendRawAndReadResponseAsync(pipeName, "\n");

        Assert.False(response.Ok);
        Assert.Equal(2, response.ExitCode);
    }

    // ---------- 管道名派生 ----------

    [Fact]
    public void PipeNames_ForCurrentUser_StableAndPrefixedAndUserTagged()
    {
        var first = CliPipeNames.ForCurrentUser();
        var second = CliPipeNames.ForCurrentUser();
        Assert.Equal(first, second);
        Assert.StartsWith("prtsc-cli-", first);
        Assert.NotEqual("prtsc-cli-", first); // 用户隔离段存在（tag 或 tmpdir 变体）
        Assert.False(first.Contains(Path.DirectorySeparatorChar), "名字本身不应含路径分隔符");
    }

    // ---------- helpers ----------

    /// <summary>裸管道客户端：写任意行，读回一帧响应（绕过 CliPipeClient，测试坏帧路径）。</summary>
    private static async Task<CliPipeResponse> SendRawAndReadResponseAsync(string pipeName, string line)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000);
        var request = Encoding.UTF8.GetBytes(line);
        await pipe.WriteAsync(request);
        await pipe.FlushAsync();

        // CliPipeFrame 是 internal，这里手动读帧（测试工程无法 InternalsVisibleTo？——直接读到 EOF/换行）
        var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var read = await pipe.ReadAsync(chunk);
            if (read == 0) break;
            var terminator = Array.IndexOf(chunk, (byte)'\n', 0, read);
            if (terminator >= 0)
            {
                buffer.Write(chunk, 0, terminator);
                break;
            }
            buffer.Write(chunk, 0, read);
        }

        Assert.True(CliPipeProtocol.TryDecodeResponse(buffer.ToArray(), out var response, out var error),
            $"Malformed response: {error}");
        return response!;
    }
}
