using Prtsc.Core.Capture;
using Xunit;

namespace Prtsc.Core.Tests;

/// <summary>
/// D-1 倒计时编排器（review C-1 回归主场）：
/// 到点后的 settle 间隙内若发生新触发——delay=0 清 pending / 带延时开新一轮覆盖 pending——
/// 旧 finisher 都不得分发；新一轮接管时旧 finisher 不得误关它的角标。
/// 测试直接调 OnCompletedAsync(round) 模拟 App 的「Completed → marshal → UI 线程」接线，
/// settle 延迟注入可控门闩，竞态场景决定性复现（不依赖真实时序）。
/// </summary>
public class CountdownOrchestratorTests
{
    /// <summary>可手动放行的 settle 延迟（模拟 250ms 间隙，测试决定性控制恢复时机）。</summary>
    private sealed class GateDelay
    {
        private readonly List<TaskCompletionSource> _gates = new();

        public Task Wait(TimeSpan _)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gates.Add(tcs);
            return tcs.Task;
        }

        public void ReleaseAll()
        {
            foreach (var gate in _gates) gate.TrySetResult();
        }
    }

    /// <summary>测试用虚拟调度器（与 DelayedCaptureCountdownTests 同款）。</summary>
    private sealed class VirtualToken(Action callback) : ICountdownToken
    {
        public bool IsDisposed { get; private set; }
        public void Fire() { if (!IsDisposed) callback(); }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class VirtualScheduler : ICountdownScheduler
    {
        public List<VirtualToken> Tokens { get; } = new();
        public ICountdownToken Schedule(TimeSpan delay, Action callback)
        {
            var token = new VirtualToken(callback);
            Tokens.Add(token);
            return token;
        }
        public void FirePending()
        {
            var token = Tokens.LastOrDefault(t => !t.IsDisposed)
                        ?? throw new InvalidOperationException("没有待触发的定时器");
            token.Fire();
        }
    }

    private sealed class Harness
    {
        public VirtualScheduler Scheduler { get; } = new();
        public GateDelay Gate { get; } = new();
        public List<string> Dispatched { get; } = new();
        public List<string> Badge { get; } = new();
        public CountdownOrchestrator<string> Orchestrator { get; set; }

        public Harness()
        {
            var countdown = new DelayedCaptureCountdown(Scheduler);
            Orchestrator = new CountdownOrchestrator<string>(
                countdown,
                intent => Dispatched.Add(intent),
                settleDelay: TimeSpan.FromMilliseconds(250),
                delay: Gate.Wait,
                onStarted: total => Badge.Add($"show:{total}"),
                onTick: remaining => Badge.Add($"tick:{remaining}"),
                onBadgeClose: () => Badge.Add("close"));
        }
    }

    [Fact]
    public async Task SettleGap_OverwrittenByNewDelayedRound_OldFinisherDoesNotDispatch()
    {
        // review C-1 回归（钉住用例）：round1 到点进入 settle → 间隙内新延时轮覆盖 pending（角标重现）→
        // 旧 finisher 恢复后既不分发新意图、也不关新一轮的角标；新一轮到点后正常分发。
        var h = new Harness();

        h.Orchestrator.Start("intent-1", 1);
        Assert.Equal(["show:1"], h.Badge);

        h.Scheduler.FirePending(); // round1 到点（Completed(1)）
        var finish1 = h.Orchestrator.OnCompletedAsync(1); // App marshal 后的入口；进入 settle 挂起
        Assert.Equal(["show:1", "close"], h.Badge); // 入口关的是 round1 自己的角标

        h.Orchestrator.Start("intent-2", 3); // C-1 场景：间隙内新延时触发（pending 被覆盖、角标重现）
        Assert.Equal(["show:1", "close", "show:3"], h.Badge);

        h.Gate.ReleaseAll();
        await finish1;

        Assert.Empty(h.Dispatched); // ★ 核心：旧 finisher 不得分发新一轮的意图
        Assert.Equal(["show:1", "close", "show:3"], h.Badge); // 也不得误关新一轮角标

        // 新一轮正常走完 → 正常分发
        h.Scheduler.FirePending();
        h.Scheduler.FirePending();
        h.Scheduler.FirePending();
        var finish2 = h.Orchestrator.OnCompletedAsync(2);
        Assert.Equal(["show:1", "close", "show:3", "tick:2", "tick:1", "close"], h.Badge);

        h.Gate.ReleaseAll();
        await finish2;
        Assert.Equal(["intent-2"], h.Dispatched);
    }

    [Fact]
    public async Task SettleGap_NewRoundRunningAtEntry_OldFinisherAbortsAndKeepsNewBadge()
    {
        // 新轮在旧 finisher 入口前已启动：入口 IsRunning 防线直接作废，不关新轮角标
        var h = new Harness();

        h.Orchestrator.Start("intent-1", 1);
        h.Scheduler.FirePending(); // Completed(1)
        h.Orchestrator.Start("intent-2", 3); // 新轮先于 finisher 入口启动

        var finish1 = h.Orchestrator.OnCompletedAsync(1);
        await finish1;

        Assert.Empty(h.Dispatched);
        Assert.Equal(["show:1", "show:3"], h.Badge); // 没有 close：新轮角标未被误关
        Assert.True(h.Orchestrator.IsRunning);

        // 新轮自己到点 → 正常分发
        h.Scheduler.FirePending();
        h.Scheduler.FirePending();
        h.Scheduler.FirePending();
        var finish2 = h.Orchestrator.OnCompletedAsync(2);
        h.Gate.ReleaseAll();
        await finish2;
        Assert.Equal(["intent-2"], h.Dispatched);
        Assert.Equal(["show:1", "show:3", "tick:2", "tick:1", "close"], h.Badge);
    }

    [Fact]
    public async Task SettleGap_DelayZeroTriggerClearsPending_OldFinisherDoesNotDispatch()
    {
        // 间隙内 delay=0 直抓路径先 ClearPending 再直接分发：旧 finisher 恢复后 pending 为空，作废
        var h = new Harness();

        h.Orchestrator.Start("intent-1", 1);
        h.Scheduler.FirePending();
        var finish1 = h.Orchestrator.OnCompletedAsync(1);

        h.Orchestrator.ClearPending(); // TriggerCaptureAsync else 分支的清理防线
        h.Gate.ReleaseAll();
        await finish1;

        Assert.Empty(h.Dispatched);
    }

    [Fact]
    public async Task SettleGap_CancelDuringSettle_OldFinisherDoesNotDispatch()
    {
        // 间隙内用户取消（热键）→ Cancel 清 pending 关角标 → finisher 作废
        var h = new Harness();

        h.Orchestrator.Start("intent-1", 1);
        h.Scheduler.FirePending();
        var finish1 = h.Orchestrator.OnCompletedAsync(1);
        Assert.Equal(["show:1", "close"], h.Badge);

        h.Orchestrator.Cancel();
        Assert.Equal(["show:1", "close", "close"], h.Badge); // Cancel 再关一次（幂等语义）

        h.Gate.ReleaseAll();
        await finish1;
        Assert.Empty(h.Dispatched);
    }

    [Fact]
    public async Task NormalRound_BadgeClosesBeforeDispatch()
    {
        var h = new Harness();
        var order = new List<string>();
        h.Orchestrator = new CountdownOrchestrator<string>(
            new DelayedCaptureCountdown(h.Scheduler),
            intent => { order.Add("dispatch:" + intent); h.Dispatched.Add(intent); },
            settleDelay: TimeSpan.FromMilliseconds(250),
            delay: h.Gate.Wait,
            onStarted: total => { order.Add($"show:{total}"); h.Badge.Add($"show:{total}"); },
            onBadgeClose: () => { order.Add("close"); h.Badge.Add("close"); });

        h.Orchestrator.Start("a", 2); // 该编排器包的是全新 countdown：轮次号从 1 起
        h.Scheduler.FirePending(); // tick:1
        h.Scheduler.FirePending(); // Completed(1)
        var finish = h.Orchestrator.OnCompletedAsync(1);
        h.Gate.ReleaseAll();
        await finish;

        Assert.Equal(["show:2", "close", "dispatch:a"], order); // 角标先关、抓帧后走（快照不含倒计时 UI）
    }

    [Fact]
    public void Start_RejectsZeroOrNegative_ZeroIsDirectDispatchPath()
    {
        var h = new Harness();
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Orchestrator.Start("x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Orchestrator.Start("x", -1));
    }
}
