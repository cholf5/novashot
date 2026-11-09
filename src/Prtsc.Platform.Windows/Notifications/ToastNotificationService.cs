using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Prtsc.Platform.Windows.Notifications;

/// <summary>
/// WinRT Toast 桌面通知（N-1，Windows.UI.Notifications；TFM 自带 Windows SDK 引用面，无需新包）。
/// AUMID 固定 <c>Prtsc</c>：
/// - <b>vpk 安装版</b>（开始菜单快捷方式在册）→ toast 正常弹出；
/// - <b>未打包</b>（dotnet run / 便携 zip 无快捷方式）→ AUMID 未注册，CreateToastNotifier/Show
///   可能抛——实现内 try-catch 降级为日志（fire-and-forget 契约），绝不影响导出/复制/上传主流程。
/// Toast XML 拼装在 Core 纯函数 <see cref="ToastXml"/>（Core.Tests 钉死）；
/// 点击 toast（launch 属性带 filePath）→ <c>explorer /select,&lt;path&gt;</c> 打开文件所在目录（尽力而为）。
/// </summary>
public sealed class ToastNotificationService : INotificationService
{
    /// <summary>
    /// Toast AUMID。与包身份/开始菜单项对齐（vpk 安装版可弹）；未打包机器上该 AUMID 未注册 → Show 时降级为日志。
    /// </summary>
    public const string AppUserModelId = "Prtsc";

    private readonly ILogger<ToastNotificationService>? _logger;

    public ToastNotificationService(ILogger<ToastNotificationService>? logger = null)
    {
        _logger = logger;
    }

    public void Show(string title, string message, string? filePath = null)
    {
        try
        {
            var document = new XmlDocument();
            document.LoadXml(ToastXml.Build(title, message, filePath));
            var toast = new ToastNotification(document);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                // SDK 投影把 Activated 的第二参型为 object（非泛型历史签名）→ 运行时 as 回 ToastActivatedEventArgs
                toast.Activated += (_, args) =>
                    OpenContainingFolder((args as ToastActivatedEventArgs)?.Arguments);
            }
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
        }
        catch (Exception ex)
        {
            // fire-and-forget 契约：AUMID 未注册（未打包）/通知被系统禁用/XML 拒收 → 只记日志，不抛
            _logger?.LogWarning(ex, "Toast notification failed (title={Title}); continuing silently", title);
        }
    }

    /// <summary>
    /// toast 激活回调 → <c>explorer /select,"&lt;path&gt;"</c> 选中文件（即打开所在目录）。
    /// 尽力而为：launch 参数为空（上传/复制类通知）或 explorer 启动失败一律静默。
    /// 回调在 WinRT 线程触发，Process.Start 线程安全，无需 marshal。
    /// </summary>
    private void OpenContainingFolder(string? arguments)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments)) return;
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{arguments}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Open containing folder failed (path={Path})", arguments);
        }
    }
}
