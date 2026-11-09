namespace Prtsc.Core.Notifications;

/// <summary>
/// N-1：可触发桌面通知的成功事件。本卡四处触发面：导出成功 / QuickSave 落盘 /
/// 复制到剪贴板 / 上传成功。per-event 开关是预留扩展位（见 <see cref="NotificationPolicy"/>），
/// 当前生效的只有 <c>StartupConfig.ShowNotification</c> 总开关。
/// </summary>
public enum NotificationEvent
{
    Exported,
    QuickSaved,
    Copied,
    Uploaded,
}
