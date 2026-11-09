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
        await WaitUntilProbeAsync(pipeName);

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
        await WaitUntilProbeAsync(pipeName);

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
        await WaitUntilProbeAsync(pipeName);

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
        await WaitUntilProbeAsync(pipeName);

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
        await WaitUntilProbeAsync(pipeName);

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

    // ---------- 廉价探测闸门（review I-1 回归） ----------

    [Fact]
    public void CheapProbe_NonexistentPipe_ReturnsFalse()
    {
        Assert.False(CliPipeClient.CheapProbe(UniquePipeName()));
    }

    [Fact]
    public async Task CheapProbe_LiveServer_ReturnsTrue()
    {
        var pipeName = UniquePipeName();
        using var host = new CliPipeServerHost(pipeName, (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));
        host.Start();
        try
        {
            await WaitUntilProbeAsync(pipeName);
            Assert.True(CliPipeClient.CheapProbe(pipeName));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task TryForward_NoInstance_DoesNotPayFullConnectTimeout()
    {
        // 回归（review I-1）：无实例时不得为单实例探测支付全额连接等待——
        // connect 超时故意给 10s：闸门失效（直接进 ConnectAsync 全额轮询）时本用例耗 ≥10s 必挂；
        // 闸门生效立即返回。计时断言程度：证明走的是廉价闸门而非连接等待（不承诺精确毫秒）。
        var pipeName = UniquePipeName(); // 从未创建服务端
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"], connectTimeoutMs: 10_000);
        sw.Stop();

        Assert.False(result.Connected);
        Assert.True(sw.ElapsedMilliseconds < 2_000,
            $"No-instance forward took {sw.ElapsedMilliseconds} ms despite 10s connect timeout — probe gate not effective.");
    }

    [Fact]
    public async Task TryForward_StaleSocketFile_FailsFastNotTimeout()
    {
        // review 实测场景：宿主被杀后 Unix 残留 CoreFxPipe_* socket 文件。此处放一个同名普通文件
        // 模拟「闸门命中但不是活 socket」：connect 必须立即失败（ENOTSOCK/ENOENT 或超时兜底），
        // 不得挂满超时（gui 冷启动传 GuiConnectTimeoutMs=100ms，这里给 3s 余量验证远小于超时）。
        // Windows 无 socket 文件概念，该文件对闸门无影响 → 走 skip 分支，同样快速返回。
        var pipeName = UniquePipeName();
        var stalePath = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName);
        await File.WriteAllTextAsync(stalePath, "not a socket");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await CliPipeClient.TryForwardAsync(pipeName, ["full"],
                connectTimeoutMs: CliPipeClient.GuiConnectTimeoutMs, responseTimeoutMs: 3000);
            sw.Stop();

            Assert.False(result.Connected);
            Assert.True(sw.ElapsedMilliseconds < 3_000,
                $"Stale-entry forward took {sw.ElapsedMilliseconds} ms — stale file must fail fast, not wait the timeout.");
        }
        finally
        {
            File.Delete(stalePath);
        }
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
        await WaitUntilProbeAsync(pipeName);
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

    /// <summary>
    /// 等宿主的管道入口出现（review I-1 闸门生效后的测试义务）：CliPipeServerHost.Start() 是
    /// fire-and-forget，服务端流在后台 Task 里创建——廉价闸门探不到入口时 TryForwardAsync 会
    /// 立即判 NoInstance（这正是它的职责）。生产语境 CLI 在 GUI 启动数秒后才调用，无此竞态；
    /// 测试里轮询闸门到位再转发，等价于模拟「实例已起来」。
    /// </summary>
    private static async Task WaitUntilProbeAsync(string pipeName, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!CliPipeClient.CheapProbe(pipeName))
        {
            Assert.True(sw.ElapsedMilliseconds < timeoutMs, $"Server pipe entry never appeared within {timeoutMs} ms.");
            await Task.Delay(10);
        }
    }

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
