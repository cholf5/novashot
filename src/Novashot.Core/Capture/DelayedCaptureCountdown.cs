namespace Novashot.Core.Capture;

/// <summary>
/// 一次性定时器调度抽象：倒计时状态机用它隔离真实时间。
/// 生产环境用 <see cref="CountdownScheduler"/>（System.Threading.Timer，回调在任意线程池线程）；
/// 测试注入虚拟调度器手动推进；L-1 CLI（headless）可直接复用本机制做 <c>--delay</c>。
/// </summary>
public interface ICountdownScheduler
{
    /// <summary>安排一个 delay 后的一次性回调（回调线程由实现决定，可能不是调用线程），返回可取消句柄。</summary>
    ICountdownToken Schedule(TimeSpan delay, Action callback);
}

/// <summary>一次已安排的定时调用；Dispose 后回调不再执行（已飞行中的回调由状态机锁内 IsRunning 防线兜底）。</summary>
public interface ICountdownToken : IDisposable;

/// <summary>System.Threading.Timer 包装的真实调度器（回调在线程池线程）。</summary>
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
/// <item><see cref="Round"/>：每次 Start 递增的轮次号；<see cref="Completed"/> 携带自己那一轮的号，调用方用它绑定 pending 意图（App 层 250ms 间隙竞态防线，C-1）。</item>
/// </list>
/// 线程模型（review I-1）：内部加锁，Start/Cancel 与定时器回调可在任意线程并发调用，状态迁移原子；
/// 事件在锁内触发（Monitor 可重入，处理方再入 Start/Cancel 安全，但不得阻塞等待依赖本锁的其他线程）。
/// 保证：Cancel 返回后不会再收到本轮任何事件；Completed 每轮至多一次且到达时 IsRunning 已为 false。
/// </summary>
public sealed class DelayedCaptureCountdown
{
    private readonly object _gate = new();
    private readonly ICountdownScheduler _scheduler;
    private readonly TimeSpan _tickInterval;
    private ICountdownToken? _token;

    private long _round;
    private int _totalSeconds;
    private int _remainingSeconds;
    private bool _isRunning;

    /// <param name="scheduler">定时调度抽象（默认真实 System.Threading.Timer）。</param>
    /// <param name="tickInterval">逐秒间隔；默认 1 秒（测试压测可调小）。</param>
    public DelayedCaptureCountdown(ICountdownScheduler? scheduler = null, TimeSpan? tickInterval = null)
    {
        _scheduler = scheduler ?? CountdownScheduler.Instance;
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>轮次号（每次 Start 递增；Completed 事件携带所属轮次）。</summary>
    public long Round { get { lock (_gate) return _round; } }

    /// <summary>本轮计划总秒数（Running 中才有意义）。</summary>
    public int TotalSeconds { get { lock (_gate) return _totalSeconds; } }

    /// <summary>剩余秒数（Running 中 &gt; 0；到点/取消后归 0）。</summary>
    public int RemainingSeconds { get { lock (_gate) return _remainingSeconds; } }

    public bool IsRunning { get { lock (_gate) return _isRunning; } }

    /// <summary>开始一轮倒计时（参数 = 计划总秒数）。</summary>
    public event EventHandler<int>? Started;

    /// <summary>逐秒回调（参数 = 剩余秒数，从 TotalSeconds-1 递减到 1）。</summary>
    public event EventHandler<int>? Tick;

    /// <summary>到点（或 0 秒直通）。参数 = 所属轮次号；到达时 IsRunning 已为 false。</summary>
    public event EventHandler<long>? Completed;

    /// <summary>仅显式 <see cref="Cancel"/> 在 Running 中触发；Start 覆盖不触发。</summary>
    public event EventHandler? Cancelled;

    /// <summary>启动一轮倒计时；seconds ≤ 0 直通 Completed。重复 Start 覆盖前一轮。</summary>
    public void Start(int seconds)
    {
        lock (_gate)
        {
            StopTokenLocked(); // 覆盖前一轮：旧定时器销毁、Running 清零（不触发 Cancelled）
            _round++;

            if (seconds <= 0)
            {
                // 零秒直通：不进入 Running，同步回调一次
                Completed?.Invoke(this, _round);
                return;
            }

            _totalSeconds = seconds;
            _remainingSeconds = seconds;
            _isRunning = true;
            Started?.Invoke(this, seconds);
            ScheduleNextTickLocked();
        }
    }

    /// <summary>取消本轮（未 Running 时安全空转）。返回后保证不再收到本轮任何事件。</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            var wasRunning = _isRunning;
            StopTokenLocked();
            if (wasRunning)
                Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleNextTickLocked()
        => _token = _scheduler.Schedule(_tickInterval, OnTick);

    private void OnTick()
    {
        lock (_gate)
        {
            // 迟到回调防线：Cancel/新 Start 已把状态清掉（token 可能已 Dispose 或本回调飞行中状态被翻）
            if (!_isRunning) return;

            // 本 tick 的定时器已消费，及时释放
            _token?.Dispose();
            _token = null;

            _remainingSeconds--;
            if (_remainingSeconds > 0)
            {
                Tick?.Invoke(this, _remainingSeconds);
                ScheduleNextTickLocked();
                return;
            }

            _isRunning = false;
            _totalSeconds = 0;
            Completed?.Invoke(this, _round);
        }
    }

    private void StopTokenLocked()
    {
        _token?.Dispose();
        _token = null;
        _isRunning = false;
        _totalSeconds = 0;
        _remainingSeconds = 0;
    }
}
