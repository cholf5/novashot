namespace Novashot.Core.Notifications;

/// <summary>
/// 平台无关的桌面通知能力（N-1）。由 Novashot.Platform.Windows 提供 WinRT Toast 实现
/// （<c>ToastNotificationService</c>）；Core 提供 Null 静默实现作为 mac/linux 与
/// 一切无通知环境的默认装配。
/// 语义：<b>fire-and-forget</b>——实现必须自吞一切异常（系统禁用通知 / AUMID 未注册 /
/// XML 拒收都只降级为日志），调用点不 try-catch、不 await；通知失败绝不影响
/// 导出/复制/上传主流程。
/// </summary>
public interface INotificationService
{
    /// <summary>弹一条系统通知。title/message 由调用方本地化；<paramref name="filePath"/>
    /// 可选（QuickSave/导出传落盘路径，平台实现尽力支持「点击通知打开所在目录」）。</summary>
    void Show(string title, string message, string? filePath = null);
}

/// <summary>静默实现（Core 默认）：什么都不做、永远不抛。mac/linux 本卡装配；测试用。</summary>
public sealed class NullNotificationService : INotificationService
{
    public static NullNotificationService Instance { get; } = new();

    public void Show(string title, string message, string? filePath = null)
    {
    }
}
