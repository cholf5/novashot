using Prtsc.App.Cli;
using Prtsc.Core.Capture;
using Prtsc.Core.Cli;
using Prtsc.Core.Common;
using Prtsc.Core.Hotkeys;

namespace Prtsc.App.Tests;

/// <summary>
/// L-1 GUI 侧 CLI 接收分发（CliRequestRouter）——App.axaml.cs 管道服务端的 handler 语义：
/// gui → activate；region 缺 geometry / window → 交互捕获触发（含 --delay 透传）；
/// full / region+geometry → headless 导出执行器（App 注入侧挂在后台线程，本测试用替身断言透传）。
/// 纯类分发，无 Avalonia 对象，headless 下直接可测。
/// </summary>
public class CliRequestRouterTests
{
    [Fact]
    public async Task GuiCommand_TriggersActivate()
    {
        var activated = 0;
        var router = NewRouter(activate: () => activated++);

        var response = await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Gui });

        Assert.Equal(1, activated);
        Assert.True(response.Ok);
        Assert.Equal(0, response.ExitCode);
        Assert.Contains("activated", response.Message);
    }

    [Fact]
    public async Task RegionWithoutGeometry_TriggersInteractiveRegionWithDelay()
    {
        HotkeyAction? action = null;
        int? delay = null;
        var headlessCalled = 0;
        var router = NewRouter(
            trigger: (a, d) => { action = a; delay = d; },
            headless: (_, _) => { headlessCalled++; return Task.FromResult(CliPipeResponse.Success("unused")); });

        var response = await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Region, DelaySeconds = 3 });

        Assert.Equal(HotkeyAction.Region, action);
        Assert.Equal(3, delay);
        Assert.Equal(0, headlessCalled);
        Assert.True(response.Ok);
        Assert.Contains("Region", response.Message);
    }

    [Fact]
    public async Task WindowCommand_TriggersInteractiveWindow()
    {
        HotkeyAction? action = null;
        var router = NewRouter(trigger: (a, _) => action = a);

        var response = await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Window, DelaySeconds = 0 });

        Assert.Equal(HotkeyAction.Window, action);
        Assert.True(response.Ok);
        Assert.Contains("Window", response.Message);
    }

    [Fact]
    public async Task FullCommand_GoesToHeadlessExecutor()
    {
        CliCommand? received = null;
        var router = NewRouter(headless: (cmd, _) =>
        {
            received = cmd;
            return Task.FromResult(CliPipeResponse.Success("Saved to /tmp/x.png (100 bytes)"));
        });

        var response = await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Full, Clipboard = true });

        Assert.NotNull(received);
        Assert.Equal(CliCaptureMode.Full, received!.Mode);
        Assert.True(response.Ok);
        Assert.Equal("Saved to /tmp/x.png (100 bytes)", response.Message);
    }

    [Fact]
    public async Task RegionWithGeometry_GoesToHeadlessExecutor()
    {
        CliCommand? received = null;
        var router = NewRouter(headless: (cmd, _) =>
        {
            received = cmd;
            return Task.FromResult(CliPipeResponse.Success("ok"));
        });
        var rect = new PixelRect(1, 2, 3, 4);

        await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Region, Geometry = rect });

        Assert.NotNull(received);
        Assert.Equal(rect, received!.Geometry);
    }

    [Fact]
    public async Task HeadlessExecutorThrows_RespondsFailureNotThrow()
    {
        var router = NewRouter(headless: (_, _) => throw new InvalidOperationException("capture exploded"));

        var response = await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Full });

        Assert.False(response.Ok);
        Assert.Equal(1, response.ExitCode);
        Assert.Contains("capture exploded", response.Message);
    }

    [Fact]
    public async Task HeadlessExecutor_ReceivesCancellationToken_Passthrough()
    {
        CancellationToken seen = default;
        var router = NewRouter(headless: (_, ct) =>
        {
            seen = ct;
            return Task.FromResult(CliPipeResponse.Success("ok"));
        });
        using var cts = new CancellationTokenSource();

        await router.HandleAsync(new CliCommand { Mode = CliCaptureMode.Full }, cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    private static CliRequestRouter NewRouter(
        CliRequestRouter.ActivateHandler? activate = null,
        CliRequestRouter.InteractiveTriggerHandler? trigger = null,
        CliRequestRouter.HeadlessExecutorHandler? headless = null) => new(
        activate ?? (() => { }),
        trigger ?? ((_, _) => { }),
        headless ?? ((_, _) => Task.FromResult(CliPipeResponse.Success("stub"))));
}

/// <summary>
/// L-1 管道服务端宿主装配（headless 下不炸）：按 App.axaml.cs 的真实组合方式
/// （宿主 + CliRequestRouter 作为 handler）起服务端，客户端转发一整条命令，断言端到端分发结果。
/// </summary>
public class CliPipeHostAssemblyTests
{
    private static string UniquePipeName() => $"prtsc-cli-t{Guid.NewGuid():N}".AsSpan(0, 17).ToString();

    [Fact]
    public async Task HostWithRouter_ForwardedWindowCommand_TriggersInteractiveAndRespondsOk()
    {
        var pipeName = UniquePipeName();
        var interactive = new List<HotkeyAction>();
        var router = new CliRequestRouter(
            activate: () => { },
            triggerInteractive: (action, _) => interactive.Add(action),
            executeHeadless: (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));

        using var host = new CliPipeServerHost(pipeName, (cmd, ct) => router.HandleAsync(cmd, ct));
        host.Start();

        var result = await CliPipeClient.TryForwardAsync(
            pipeName, ["window", "--delay", "3"], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal([HotkeyAction.Window], interactive);
    }

    [Fact]
    public async Task HostWithRouter_ForwardedGuiCommand_ActivatesOnly()
    {
        var pipeName = UniquePipeName();
        var activated = false;
        var interactive = new List<HotkeyAction>();
        var router = new CliRequestRouter(
            activate: () => activated = true,
            triggerInteractive: (action, _) => interactive.Add(action),
            executeHeadless: (_, _) => Task.FromResult(CliPipeResponse.Success("unused")));

        using var host = new CliPipeServerHost(pipeName, (cmd, ct) => router.HandleAsync(cmd, ct));
        host.Start();

        var result = await CliPipeClient.TryForwardAsync(pipeName, [], connectTimeoutMs: 2000);

        Assert.True(result.Connected);
        Assert.Equal(0, result.ExitCode);
        Assert.True(activated);
        Assert.Empty(interactive);
    }
}
