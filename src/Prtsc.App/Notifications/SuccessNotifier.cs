using Microsoft.Extensions.Logging;
using Prtsc.Core.Config;
using Prtsc.Core.Notifications;

namespace Prtsc.App.Notifications;

/// <summary>
/// N-1：App 层唯一的通知出口。policy 判定（<see cref="NotificationPolicy"/> × config 总开关）
/// 与服务调用集中在这一个函数——VM/View 各触发点只报「事件 + 文案 + 可选路径」，不写
/// if-else 长链。「失败不扰」也在这集中兜底：config 读取 / policy / 服务本身的任何异常
/// 只记 Debug 日志，绝不影响导出/复制/上传主流程（与服务的 fire-and-forget 契约双保险）。
/// </summary>
internal static class SuccessNotifier
{
    public static void Notify(
        INotificationService? service,
        IConfigStore? configStore,
        NotificationEvent evt,
        string title,
        string message,
        string? filePath = null,
        ILogger? logger = null)
    {
        try
        {
            if (service is null) return; // DI 外（旧构造/部分测试）无服务 → 静默
            var setting = configStore?.Current.Startup.ShowNotification ?? true;
            if (!NotificationPolicy.ShouldNotify(setting, evt)) return;
            service.Show(title, message, filePath);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Notification suppressed ({Event})", evt);
        }
    }
}
