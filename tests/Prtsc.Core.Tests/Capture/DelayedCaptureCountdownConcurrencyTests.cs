using Prtsc.Core.Capture;
using Xunit;

namespace Prtsc.Core.Tests;

/// <summary>
/// review I-1 回归：状态机加锁的并发契约（真实调度器，回调在线程池线程）。
/// 可决定性钉住的锁性质：事件在锁内触发 ⇒ OnTick 的「检查→状态迁移→事件」对 Cancel 原子——
/// <see cref="EventHandlers_RunUnderLock_CancelWaitsForInFlightHandler"/> 用慢 handler + 并发 Cancel
/// 断言 Cancel 必须等飞行中的 handler 完成（无锁时该断言必挂，已做过去锁验证）。
/// <see cref="Cancel_ConcurrentWithTicks_NoEventsAfterCancelReturns"/> 钉「Cancel 返回 + settle 后零事件」契约
/// （能钉住 IsRunning 防线被删的回归；锁本身的移除因碰撞窗口仅数纳秒、无内部 hook 而无法经验钉住，
/// 由上一条的原子性断言 + 代码结构（全部迁移共用一把 Monitor 锁）保证）。
/// </summary>
public class DelayedCaptureCountdownConcurrencyTests
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task EventHandlers_RunUnderLock_CancelWaitsForInFlightHandler()
    {
        // I-1 决定性回归：Tick handler（锁内）执行期间，另一线程的 Cancel 必须被锁挡住；
        // handler 释放后 Cancel 才返回，且返回后本轮再无事件（状态迁移原子，不会在 OnTick 中段插入 Cancel）。
        var countdown = new DelayedCaptureCountdown(CountdownScheduler.Instance, TickInterval);
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventsAfterHandler = 0;

        countdown.Tick += (_, _) =>
        {
            handlerEntered.TrySetResult();
            releaseHandler.Task.Wait(); // 模拟慢 handler：拉长锁内执行段
        };
        countdown.Completed += (_, _) => Interlocked.Increment(ref eventsAfterHandler);

        countdown.Start(100);
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)); // 等 tick 进入 handler（持有锁）

        var cancelTask = Task.Run(countdown.Cancel);
        await Task.Delay(30);
        Assert.False(cancelTask.IsCompleted,
            "Cancel 在锁内 handler 未完成时就返回了——状态迁移不再原子（I-1 锁失效）");

        releaseHandler.TrySetResult();
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(2)); // handler 释放后 Cancel 才能完成
        await Task.Delay(TickInterval + TimeSpan.FromMilliseconds(30)); // settle

        Assert.Equal(0, Volatile.Read(ref eventsAfterHandler)); // Cancel 后静默
        Assert.False(countdown.IsRunning);
        Assert.Equal(0, countdown.RemainingSeconds);
    }

    [Fact]
    public async Task Cancel_ConcurrentWithTicks_NoEventsAfterCancelReturns()
    {
        // 并发敲打：Cancel 与定时回调竞速。硬不变量 = Cancel 返回（+settle）后零事件、剩余秒数永不 <1、
        // 单轮 Completed 至多一次。能钉住 IsRunning 防线被删的回归（迟到回调会发声）。
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var countdown = new DelayedCaptureCountdown(CountdownScheduler.Instance,
                TimeSpan.FromMilliseconds(5));
            var events = 0;
            var badRemaining = 0;
            var completedRounds = new List<long>();

            countdown.Tick += (_, remaining) =>
            {
                Interlocked.Increment(ref events);
                if (remaining < 1) Interlocked.Increment(ref badRemaining);
            };
            countdown.Completed += (_, round) =>
            {
                Interlocked.Increment(ref events);
                lock (completedRounds) completedRounds.Add(round);
            };

            countdown.Start(500); // 远长于本测试，等价「一直跑到 Cancel」

            // 抖动落在首个 tick（5ms）附近，制造与回调的真实并发
            await Task.Delay(TimeSpan.FromMilliseconds(2 + Random.Shared.Next(0, 6)));

            countdown.Cancel();
            var eventsAtCancel = Volatile.Read(ref events);
            await Task.Delay(40); // settle：让已起飞的迟到回调全部落地

            Assert.True(Volatile.Read(ref events) == eventsAtCancel,
                $"iteration {iteration}: Cancel 返回后仍有事件（防线失效）");
            Assert.Equal(0, Volatile.Read(ref badRemaining));
            Assert.True(completedRounds.Distinct().Count() == completedRounds.Count,
                $"iteration {iteration}: 同一轮 Completed 触发多次");
            Assert.Equal(0, countdown.RemainingSeconds);
            Assert.False(countdown.IsRunning);
        }
    }

    [Fact]
    public async Task ConcurrentStarts_EachRoundCompletesExactlyOnce()
    {
        // 交替覆盖 + 真实定时：每个走完的轮次 Completed 恰好一次，轮次号单调递增
        var countdown = new DelayedCaptureCountdown(CountdownScheduler.Instance, TickInterval);
        var completedRounds = new List<long>();
        countdown.Completed += (_, round) =>
        {
            lock (completedRounds) completedRounds.Add(round);
        };

        for (var i = 0; i < 8; i++)
        {
            countdown.Start(50); // 覆盖上一轮
            await Task.Delay(TickInterval * 3);
        }
        countdown.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(80));

        Assert.Equal(completedRounds.Distinct().Count(), completedRounds.Count);
        Assert.Equal(completedRounds.OrderBy(r => r), completedRounds);
        Assert.Equal(0, countdown.RemainingSeconds);
    }
}
