namespace Prtsc.Core.Capture;

/// <summary>
/// 一次性定时器调度抽象：倒计时状态机用它隔离真实时间。
/// 生产环境用 <see cref="CountdownScheduler"/>（System.Threading.Timer）；
/// 测试注入虚拟调度器手动推进；L-1 CLI（headless）可直接复用本机制做 <c>--delay</c>。
/// 注意：Start/Cancel 与回调须在同一线程上下文串行调用（App 内 = UI 线程），本类型不做线程同步。
/// </summary>
public interface ICountdownScheduler
{
    /// <summary>安排一个 delay 后的一次性回调，返回可取消句柄。</summary>
    ICountdownToken Schedule(TimeSpan delay, Action callback);
}

/// <summary>一次已安排的定时调用；Dispose 后回调不再执行（已飞行中的回调由状态机侧 IsRunning 防线兜底）。</summary>
public interface ICountdownToken : IDisposable;

/// <summary>System.Threading.Timer 包装的真实调度器。</summary>
public sealed class CountdownScheduler : ICountdownScheduler
{
    public static CountdownScheduler Instance { get; } = new();

    public ICountdownToken Schedule(TimeSpan delay, Action callback)
    {
        var timer = new System.Threading.Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
        return new TimerToken(timer);
    }

    private sealed class TimerToken(System.Threading.Timer timer) : ICountdownToken
    {
        public void Dispose() => timer.Dispose();
    }
}

/// <summary>
/// D-1 延时截图倒计时状态机（纯逻辑，无 UI 依赖）。
/// <list type="bullet">
/// <item><see cref="Start"/>：0/负数秒 = 直通（同步触发一次 Completed，不进入 Running）；正数秒 = 进入 Running，逐秒 <see cref="Tick"/>，到点 <see cref="Completed"/>。</item>
/// <item>Running 中再次 <see cref="Start"/> = 覆盖前一轮（旧轮定时器销毁，不触发 <see cref="Cancelled"/>）。</item>
/// <item><see cref="Cancel"/>：仅 Running 中触发 <see cref="Cancelled"/>；空转安全（无事件无异常）。</item>
/// </list>
/// </summary>
public sealed class DelayedCaptureCountdown
{
    private readonly ICountdownScheduler _scheduler;
    private ICountdownToken? _token;

    public DelayedCaptureCountdown(ICountdownScheduler? scheduler = null)
        => _scheduler = scheduler ?? CountdownScheduler.Instance;

    /// <summary>本轮计划总秒数（Running 中才有意义）。</summary>
    public int TotalSeconds { get; private set; }

    /// <summary>剩余秒数（Running 中 &gt; 0；到点/取消后归 0）。</summary>
    public int RemainingSeconds { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>开始一轮倒计时（参数 = 计划总秒数）。</summary>
    public event EventHandler<int>? Started;

    /// <summary>逐秒回调（参数 = 剩余秒数，从 TotalSeconds-1 递减到 1）。</summary>
    public event EventHandler<int>? Tick;

    /// <summary>到点（或 0 秒直通）。IsRunning 已为 false。</summary>
    public event EventHandler? Completed;

    /// <summary>仅显式 <see cref="Cancel"/> 在 Running 中触发；Start 覆盖不触发。</summary>
    public event EventHandler? Cancelled;

    /// <summary>启动一轮倒计时；seconds ≤ 0 直通 Completed。重复 Start 覆盖前一轮。</summary>
    public void Start(int seconds)
    {
        StopToken(); // 覆盖前一轮：旧定时器销毁、Running 清零（不触发 Cancelled）

        if (seconds <= 0)
        {
            // 零秒直通：不进入 Running，同步回调一次
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        TotalSeconds = seconds;
        RemainingSeconds = seconds;
        IsRunning = true;
        Started?.Invoke(this, seconds);
        ScheduleNextTick();
    }

    /// <summary>取消本轮（未 Running 时安全空转）。</summary>
    public void Cancel()
    {
        var wasRunning = IsRunning;
        StopToken();
        if (wasRunning)
            Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleNextTick()
        => _token = _scheduler.Schedule(TimeSpan.FromSeconds(1), OnTick);

    private void OnTick()
    {
        // 真实定时器回调可能晚于 Cancel 到达（线程池竞态）；状态已清零则忽略
        if (!IsRunning) return;

        // 本 tick 的定时器已消费，及时释放
        _token?.Dispose();
        _token = null;

        RemainingSeconds--;
        if (RemainingSeconds > 0)
        {
            Tick?.Invoke(this, RemainingSeconds);
            ScheduleNextTick();
            return;
        }

        IsRunning = false;
        TotalSeconds = 0;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void StopToken()
    {
        _token?.Dispose();
        _token = null;
        IsRunning = false;
        TotalSeconds = 0;
        RemainingSeconds = 0;
    }
}
