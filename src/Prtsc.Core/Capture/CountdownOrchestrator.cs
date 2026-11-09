namespace Prtsc.Core.Capture;

/// <summary>
/// D-1 倒计时编排器：把「延时意图 + 倒计时状态机 + 到点后的 settle 间隙 + 分发裁决」绑成一个可测试单元。
/// App 层只做线程 marshal 与 UI（角标/状态栏）；本类持有 pending 意图与轮次绑定，纯 Core 逻辑可全量单测。
///
/// 关键不变量（review C-1）：到点后 settle 间隙内若发生新的捕获触发——
/// ① 新触发先清 pending（delay=0 直抓路径）→ 旧 finisher 恢复后 pending 为空，作废；
/// ② 新触发带延时启动新一轮（覆盖 pending、角标重现）→ 旧 finisher 恢复后轮次不匹配，作废，
///    且入口处 <see cref="Countdown.IsRunning"/> 防线保证不会误关新一轮的角标。
/// 两条都挡住「旧 finisher 提前分发新意图、角标入快照」的竞态。
///
/// 线程模型：<see cref="Start"/>/<see cref="Cancel"/>/<see cref="ClearPending"/> 与 <see cref="OnCompletedAsync"/>
/// 须在同一同步上下文串行调用（App = UI 线程；App 负责把 Countdown 的 Completed 事件 marshal 过来）；
/// onStarted 回调在 Start 调用线程触发，onTick 回调在调度器线程触发（App 侧自行 Post）。
/// L-1 CLI 可整体复用（dispatch = headless 抓帧落盘）。
/// </summary>
public sealed class CountdownOrchestrator<TIntent>
{
    private readonly record struct PendingIntent(long Round, TIntent Intent);

    private readonly DelayedCaptureCountdown _countdown;
    private readonly Action<TIntent> _dispatch;
    private readonly TimeSpan _settleDelay;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Action<int>? _onStarted;
    private readonly Action<int>? _onTick;
    private readonly Action? _onBadgeClose;
    private PendingIntent? _pending;

    /// <param name="countdown">倒计时状态机（调用方持有订阅 Completed → marshal → <see cref="OnCompletedAsync"/>）。</param>
    /// <param name="dispatch">到点并通过裁决后的意图执行（在 OnCompletedAsync 的调用上下文里同步触发）。</param>
    /// <param name="settleDelay">角标关闭到抓帧的防残影间隙（App = 250ms）。</param>
    /// <param name="delay">间隙等待实现（默认 Task.Delay；测试注入可控门闩）。</param>
    /// <param name="onStarted">倒计时开始（参数 = 总秒数；Start 调用线程同步触发）。</param>
    /// <param name="onTick">逐秒（参数 = 剩余秒数；调度器线程触发）。</param>
    /// <param name="onBadgeClose">角标关闭（finisher 入口 / Cancel）。</param>
    public CountdownOrchestrator(DelayedCaptureCountdown countdown,
        Action<TIntent> dispatch,
        TimeSpan settleDelay,
        Func<TimeSpan, Task>? delay = null,
        Action<int>? onStarted = null,
        Action<int>? onTick = null,
        Action? onBadgeClose = null)
    {
        _countdown = countdown;
        _dispatch = dispatch;
        _settleDelay = settleDelay;
        _delay = delay ?? DefaultDelay;
        _onStarted = onStarted;
        _onTick = onTick;
        _onBadgeClose = onBadgeClose;

        _countdown.Started += (_, totalSeconds) => _onStarted?.Invoke(totalSeconds);
        _countdown.Tick += (_, remainingSeconds) => _onTick?.Invoke(remainingSeconds);
    }

    private static Task DefaultDelay(TimeSpan span) => Task.Delay(span);

    /// <summary>底层状态机（调用方订阅其 Completed 并 marshal 到自己的上下文后调 <see cref="OnCompletedAsync"/>）。</summary>
    public DelayedCaptureCountdown Countdown => _countdown;

    /// <summary>是否有倒计时正在进行。</summary>
    public bool IsRunning => _countdown.IsRunning;

    /// <summary>
    /// 启动一轮带意图的倒计时。仅接受 seconds &gt; 0——0 秒「不延时」语义由调用方直接分发，
    /// 不进倒计时（同时避免零秒直通的同步 Completed 造成重入）。
    /// </summary>
    public void Start(TIntent intent, int seconds)
    {
        if (seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), "延时必须 > 0；0 秒由调用方直接分发，不进倒计时");
        _countdown.Start(seconds);
        _pending = new PendingIntent(_countdown.Round, intent);
    }

    /// <summary>取消当前轮（含 settle 间隙内）：停状态机、清 pending、关角标。未在跑时安全空转。</summary>
    public void Cancel()
    {
        _countdown.Cancel();
        _pending = null;
        _onBadgeClose?.Invoke();
    }

    /// <summary>仅清残留意图（不碰状态机与角标）：倒计时已结束但 finisher 尚在 settle 间隙时，新触发的清理防线。</summary>
    public void ClearPending() => _pending = null;

    /// <summary>
    /// 倒计时到点处理（调用方把 Completed 事件 marshal 到自己的同步上下文后调用，参数 = 事件携带的轮次号）：
    /// 关角标 → settle 防残影间隙 → 轮次裁决 → 分发。间隙内 pending 被清（①）或被新一轮覆盖（②）都作废。
    /// </summary>
    public async Task OnCompletedAsync(long round)
    {
        if (_countdown.IsRunning)
            return; // 入口防线：新一轮已接管，角标与 pending 归它，本轮 finisher 直接作废

        _onBadgeClose?.Invoke();
        await _delay(_settleDelay);

        // C-1 防线：settle 间隙内 pending 被清（新 delay=0 触发）或被新延时轮覆盖（Round 不匹配）都不得分发
        if (_pending is not { } pending || pending.Round != round) return;
        _pending = null;
        _dispatch(pending.Intent);
    }
}
