using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Prtsc.App.Localization;
using Prtsc.App.Views;
using Prtsc.Core.Config;
using Prtsc.Core.Update;
using Velopack;

namespace Prtsc.App.Update;

/// <summary>
/// C-1/R-1 启动更新检查（App.OnFrameworkInitializationCompleted 尾部 fire-and-forget）。
/// 安全边界：policy 拒绝 / 未打包 / UpdateManager 构造失败 / 无网络 / Feed 404 / 下载失败
/// 全部只记日志（部分场景加状态栏提示），绝不崩溃、绝不阻塞启动。
///
/// Velopack 1.x API 注记：feedUrl 走 <see cref="UpdateManager"/> 构造器（不是 CheckForUpdatesAsync 参数）；
/// 「是否已打包」判据是 <c>UpdateManager.IsInstalled</c>（0.0.x 时代叫 IsPackaged）。
/// 未打包环境（开发跑 / mac·linux 腿）IsInstalled 恒 false，天然走不到网络调用。
/// </summary>
public static class StartupUpdateChecker
{
    public static async Task RunOnStartupAsync(IConfigStore store, MainWindow owner, ILogger logger)
    {
        var update = store.Current.Update;
        UpdateManager? manager = null;
        try
        {
            manager = new UpdateManager(update.FeedUrl);
        }
        catch (Exception ex)
        {
            // FeedUrl 空/非法在这层就挡下（Uri 构造失败）；locator 缺失（VelopackApp.Run 未跑）同理
            logger.LogWarning(ex, "UpdateManager init failed; startup update check skipped");
            return;
        }

        if (!UpdateCheckPolicy.ShouldCheckOnStartup(manager.IsInstalled, update.CheckOnStartup, update.FeedUrl))
            return;

        try
        {
            var info = await manager.CheckForUpdatesAsync();
            var target = info?.TargetFullRelease;
            if (target?.Version is not { } version) return; // 无更新：静默

            // SemanticVersion.ToFullString 可能带 prerelease/元数据后缀；ShouldPrompt 用 System.Version
            // 解析语义，这类串一律不提示（Core 单测钉死）。
            var targetText = version.ToFullString();
            var currentText = manager.CurrentVersion?.ToFullString() ?? "";
            if (!UpdateCheckPolicy.ShouldPrompt(targetText, currentText, update.SkippedVersion)) return;

            await Dispatcher.UIThread.InvokeAsync(() => ShowPrompt(store, manager, owner, info!, targetText, currentText, logger));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Startup update check failed");
            PostStatusHint(owner, Loc.T("Status.UpdateCheckFailed", ex.Message));
        }
    }

    /// <summary>非阻塞弹窗（Show 不是 ShowDialog）：不打断截图主流程。</summary>
    private static void ShowPrompt(IConfigStore store, UpdateManager manager, MainWindow owner,
        UpdateInfo info, string targetText, string currentText, ILogger logger)
    {
        try
        {
            var window = new UpdatePromptWindow(store, manager, info, targetText, currentText, logger);
            window.Show(owner);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to show update prompt");
        }
    }

    private static void PostStatusHint(MainWindow owner, string text)
        => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                owner.ShowStatus(text);
            }
            catch
            {
                // 状态栏提示是尽力而为，绝不因它崩溃
            }
        });
}
