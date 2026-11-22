namespace Novashot.Core.Notifications;

/// <summary>
/// N-1 通知决策（Core 纯函数，对齐 UpdateCheckPolicy 风格）：事件枚举 × 总开关 的决策表。
/// 本卡只有 <c>StartupConfig.ShowNotification</c> 总开关（默认 true）；决策表带事件入参是
/// 为 per-event 关闭预留的扩展形状——加值时只扩这张表，App 层调用点不变（App 只判 policy，
/// 不写业务 if-else 长链）。
/// </summary>
public static class NotificationPolicy
{
    /// <summary>
    /// 该事件在当前开关状态下是否应弹通知。未知事件（未来加值）沿用总开关：
    /// 宁可多提示也不静默漏报。
    /// </summary>
    public static bool ShouldNotify(bool setting, NotificationEvent evt) => evt switch
    {
        NotificationEvent.Exported => setting,
        NotificationEvent.QuickSaved => setting,
        NotificationEvent.Copied => setting,
        NotificationEvent.Uploaded => setting,
        _ => setting,
    };
}
