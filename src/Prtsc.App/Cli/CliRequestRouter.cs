using Prtsc.Core.Capture;
using Prtsc.Core.Cli;
using Prtsc.Core.Hotkeys;

namespace Prtsc.App.Cli;

/// <summary>
/// GUI 侧 CLI 命令接收分发（L-1 管道服务端的 handler）。本类自身不碰 Avalonia——
/// 三个回调由 App.axaml.cs 注入既有线程安全入口（ShowMainWindow / TriggerCaptureAsync 内部已 marshal），
/// headless 分支在管道连接的后台线程直接跑 <see cref="CliCaptureExecutor"/>（无 GUI 闪现）。
/// 分发语义（PM 裁决）：
/// - gui → activate（主窗口 Show+Activate，单实例对齐 winshot）；
/// - region 缺 geometry / window → 触发对应 GUI 交互捕获流程（含 --delay 倒计时），立即回「已启动」；
/// - full / region+geometry → 同一个 headless 导出执行器，等结果回传（CLI 退出码 = 执行结果）。
/// </summary>
public sealed class CliRequestRouter
{
    /// <summary>激活主窗口（App 注入 ShowMainWindow；其内部自行 Post UI 线程）。</summary>
    public delegate void ActivateHandler();

    /// <summary>交互捕获触发（App 注入 TriggerCaptureAsync；action 仅 Region/Window，delaySeconds=null = CLI 省略 --delay → 走 config 默认，终审 I-1 转发 ≡ 本地触发）。</summary>
    public delegate void InteractiveTriggerHandler(HotkeyAction action, int? delaySeconds);

    /// <summary>headless 导出（App 注入 DI 构造的 CliCaptureExecutor 执行）。</summary>
    public delegate Task<CliPipeResponse> HeadlessExecutorHandler(CliCommand command, CancellationToken ct);

    private readonly ActivateHandler _activate;
    private readonly InteractiveTriggerHandler _triggerInteractive;
    private readonly HeadlessExecutorHandler _executeHeadless;

    public CliRequestRouter(ActivateHandler activate,
        InteractiveTriggerHandler triggerInteractive,
        HeadlessExecutorHandler executeHeadless)
    {
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _triggerInteractive = triggerInteractive ?? throw new ArgumentNullException(nameof(triggerInteractive));
        _executeHeadless = executeHeadless ?? throw new ArgumentNullException(nameof(executeHeadless));
    }

    public async Task<CliPipeResponse> HandleAsync(CliCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command.Mode)
        {
            case CliCaptureMode.Gui:
                _activate();
                return CliPipeResponse.Success("Prtsc window activated.");

            case CliCaptureMode.Region when command.Geometry is null:
                _triggerInteractive(HotkeyAction.Region, command.DelaySeconds);
                return CliPipeResponse.Success("Region selection started in the running Prtsc instance.");

            case CliCaptureMode.Window:
                _triggerInteractive(HotkeyAction.Window, command.DelaySeconds);
                return CliPipeResponse.Success("Window capture started in the running Prtsc instance.");

            default:
                // 防御：handler 故障也以响应形式回传（App 注入的执行 lambda 自带 catch，这里兜底保证「分发不抛」）
                try
                {
                    return await _executeHeadless(command, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return CliPipeResponse.Fail(CliExitCodes.Failure, $"Command handling failed: {ex.Message}");
                }
        }
    }
}
