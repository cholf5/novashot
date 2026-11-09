using Prtsc.Core.Capture;
using Xunit;

namespace Prtsc.Core.Tests;

/// <summary>
/// D-1 延时截图倒计时状态机（虚拟调度器，不碰真实时间）。
/// 语义：Start 覆盖前一轮（不触发 Cancelled）；显式 Cancel 触发 Cancelled；
/// 0 秒（含负数）直通 Completed，不进入 Running；Cancel 空转安全。
/// L-1 CLI 将直接复用本类（headless 无 UI 依赖）。
/// </summary>
public class DelayedCaptureCountdownTests
{
    /// <summary>测试用虚拟调度器：登记每次 Schedule，手动 Fire 推进时间。</summary>
    private sealed class VirtualScheduler : ICountdownScheduler
    {
        public List<VirtualToken> Tokens { get; } = new();

        public ICountdownToken Schedule(TimeSpan delay, Action callback)
        {
            var token = new VirtualToken(delay, callback);
            Tokens.Add(token);
            return token;
        }

        /// <summary>触发当前未销毁的最后一个定时器（倒计时每秒排一个，串行推进）。</summary>
        public void FirePending()
        {
            var token = Tokens.LastOrDefault(t => !t.IsDisposed)
                        ?? throw new InvalidOperationException("没有待触发的定时器");
            token.Fire();
        }

        public int PendingCount => Tokens.Count(t => !t.IsDisposed);
    }

    private sealed class VirtualToken(TimeSpan delay, Action callback) : ICountdownToken
    {
        public TimeSpan Delay { get; } = delay;
        public bool IsDisposed { get; private set; }

        public void Fire()
        {
            if (IsDisposed) return;
            callback();
        }

        public void Dispose() => IsDisposed = true;
    }

    private static (DelayedCaptureCountdown C, VirtualScheduler S) Create()
    {
        var scheduler = new VirtualScheduler();
        return (new DelayedCaptureCountdown(scheduler), scheduler);
    }

    [Fact]
    public void Start_ThreeSeconds_TicksThenCompletes()
    {
        var (countdown, scheduler) = Create();
        var started = new List<int>();
        var ticks = new List<int>();
        var completed = 0;

        countdown.Started += (_, s) => started.Add(s);
        countdown.Tick += (_, s) => ticks.Add(s);
        countdown.Completed += (_, _) => completed++;

        countdown.Start(3);

        Assert.True(countdown.IsRunning);
        Assert.Equal(3, countdown.TotalSeconds);
        Assert.Equal(3, countdown.RemainingSeconds);
        Assert.Equal([3], started);
        Assert.Empty(ticks);
        Assert.Equal(0, completed);
        Assert.Equal(1, scheduler.PendingCount);

        scheduler.FirePending();
        Assert.Equal([2], ticks);
        Assert.True(countdown.IsRunning);

        scheduler.FirePending();
        Assert.Equal([2, 1], ticks);

        scheduler.FirePending();
        Assert.Equal(1, completed);
        Assert.False(countdown.IsRunning);
        Assert.Equal(0, countdown.RemainingSeconds);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Start_ZeroOrNegative_CompletesImmediatelyWithoutRunning(int seconds)
    {
        var (countdown, scheduler) = Create();
        var started = 0;
        var completed = 0;
        countdown.Started += (_, _) => started++;
        countdown.Completed += (_, _) => completed++;

        countdown.Start(seconds);

        Assert.Equal(1, completed); // 直通：同步回调一次
        Assert.Equal(0, started);   // 不进入 Running，不发 Started
        Assert.False(countdown.IsRunning);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void Cancel_Midway_StopsWithoutCompleted()
    {
        var (countdown, scheduler) = Create();
        var completed = 0;
        var cancelled = 0;
        countdown.Completed += (_, _) => completed++;
        countdown.Cancelled += (_, _) => cancelled++;

        countdown.Start(5);
        scheduler.FirePending();
        scheduler.FirePending();
        var pendingToken = scheduler.Tokens.Last(); // Cancel 后它会被销毁

        countdown.Cancel();

        Assert.Equal(0, completed);
        Assert.Equal(1, cancelled);
        Assert.False(countdown.IsRunning);

        // 已销毁的定时器再触发也不能复活回调（真实场景：系统定时器回调已在飞行中）
        pendingToken.Fire();
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Cancel_WhenIdle_IsSafeNoEvent()
    {
        var (countdown, _) = Create();
        var anyEvent = false;
        countdown.Started += (_, _) => anyEvent = true;
        countdown.Tick += (_, _) => anyEvent = true;
        countdown.Completed += (_, _) => anyEvent = true;
        countdown.Cancelled += (_, _) => anyEvent = true;

        countdown.Cancel();
        countdown.Cancel();

        Assert.False(countdown.IsRunning);
        Assert.False(anyEvent);
    }

    [Fact]
    public void Start_OverridesPreviousRound_CompletesOnceWithNewDuration()
    {
        var (countdown, scheduler) = Create();
        var completed = 0;
        var cancelled = 0;
        var ticks = new List<int>();
        countdown.Completed += (_, _) => completed++;
        countdown.Cancelled += (_, _) => cancelled++;
        countdown.Tick += (_, s) => ticks.Add(s);

        countdown.Start(5);
        scheduler.FirePending(); // 旧轮走到剩 4（Tick(4) 属于旧轮，合法）
        Assert.Equal([4], ticks);

        countdown.Start(3); // 覆盖：不发 Cancelled，重置状态
        Assert.True(countdown.IsRunning);
        Assert.Equal(3, countdown.TotalSeconds);
        Assert.Equal(3, countdown.RemainingSeconds);
        Assert.Equal(0, cancelled);
        Assert.Single(scheduler.Tokens, t => !t.IsDisposed); // 旧 token 已销毁（xUnit2031：用带谓词的重载）

        ticks.Clear(); // 只看新轮的 Tick 序列
        scheduler.FirePending();
        scheduler.FirePending();
        scheduler.FirePending();

        Assert.Equal([2, 1], ticks); // 旧轮的 Tick(4) 不会串进新一轮
        Assert.Equal(1, completed);
        Assert.False(countdown.IsRunning);
    }

    [Fact]
    public void Start_WhileIdle_AfterCompleted_CanRestart()
    {
        var (countdown, scheduler) = Create();
        var completed = 0;
        countdown.Completed += (_, _) => completed++;

        countdown.Start(1);
        scheduler.FirePending();
        Assert.Equal(1, completed);

        countdown.Start(2);
        scheduler.FirePending();
        scheduler.FirePending();
        Assert.Equal(2, completed);
    }

    [Fact]
    public void Tick_EachStepDecrementsRemainingByOne()
    {
        var (countdown, scheduler) = Create();
        var remaining = new List<int>();
        countdown.Tick += (_, s) => remaining.Add(countdown.RemainingSeconds);

        countdown.Start(4);
        scheduler.FirePending();
        scheduler.FirePending();
        scheduler.FirePending();

        Assert.Equal([3, 2, 1], remaining);
        Assert.Equal(1, countdown.RemainingSeconds);
    }

    // ---- 轮次号（review C-1：Completed 携带轮次，供 App 绑定 pending 意图） ----

    [Fact]
    public void Round_IncrementsPerStart_IncludingZeroPassThrough()
    {
        var (countdown, _) = Create();
        Assert.Equal(0, countdown.Round); // 初始无轮

        countdown.Start(3);
        Assert.Equal(1, countdown.Round);
        countdown.Start(0); // 零秒直通也算一轮
        Assert.Equal(2, countdown.Round);
        countdown.Start(2);
        Assert.Equal(3, countdown.Round);
    }

    [Fact]
    public void Completed_CarriesItsOwnRoundNumber()
    {
        var (countdown, scheduler) = Create();
        var rounds = new List<long>();
        countdown.Completed += (_, round) => rounds.Add(round);

        countdown.Start(2);
        scheduler.FirePending();
        scheduler.FirePending();
        Assert.Equal([1L], rounds);

        countdown.Start(2);
        scheduler.FirePending();
        scheduler.FirePending();
        Assert.Equal([1L, 2L], rounds);
    }

    [Fact]
    public void ZeroPassThrough_CompletedCarriesRound()
    {
        var (countdown, _) = Create();
        var rounds = new List<long>();
        countdown.Completed += (_, round) => rounds.Add(round);

        countdown.Start(0);
        Assert.Equal([1L], rounds);
    }

    [Fact]
    public void Start_Override_OldRoundNeverFiresCompleted()
    {
        var (countdown, scheduler) = Create();
        var rounds = new List<long>();
        countdown.Completed += (_, round) => rounds.Add(round);

        countdown.Start(5);      // round 1
        scheduler.FirePending(); // 旧轮 tick
        countdown.Start(3);      // round 2：覆盖，旧 token 销毁，旧轮 Completed 永不到来

        scheduler.FirePending();
        scheduler.FirePending();
        scheduler.FirePending();

        Assert.Equal([2L], rounds); // 只有 round 2 完成；round 1 无 Completed
    }
}
