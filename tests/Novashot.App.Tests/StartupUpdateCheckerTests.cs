using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.Update;
using Novashot.Core.Config;

namespace Novashot.App.Tests;

/// <summary>
/// C-1 启动检查的静默守卫契约（review M2/M3 跟进）：不触碰网络/-owner/未初始化的 Velopack locator
/// 就得安静返回。owner 传 null! 是刻意的——两个用例都在任何 owner 解引用点之前 return，
/// 传 null 恰好钉住「不碰 owner」这一行为契约。
/// </summary>
public class StartupUpdateCheckerTests
{
    private static JsonConfigStore CreateStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-updater-tests", Guid.NewGuid().ToString("N"));
        return new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
    }

    [Fact]
    public async Task EmptyFeedUrl_ReturnsSilently_BeforeTouchingVelopackOrOwner()
    {
        // 默认配置（FeedUrl 空）：不能走异常路径记 Warning，直接静默退出
        var store = CreateStore();
        await store.LoadAsync();
        Assert.Equal(string.Empty, store.Current.Update.FeedUrl);

        await StartupUpdateChecker.RunOnStartupAsync(store, null!, NullLogger.Instance);
    }

    [Fact]
    public async Task BlankFeedUrl_ReturnsSilently()
    {
        var store = CreateStore();
        store.Update(c => c.Update.FeedUrl = "   ");

        await StartupUpdateChecker.RunOnStartupAsync(store, null!, NullLogger.Instance);
    }

    [Fact]
    public async Task ValidFeedUrl_InTestProcess_ReturnsWithoutCrashingStartup()
    {
        // 测试进程未跑 VelopackApp.Build().Run() → locator 未设 → UpdateManager 构造抛异常 →
        // 被 StartupUpdateChecker 的构造 catch 兜住记日志并 return：启动路径不可被更新检查破坏。
        var store = CreateStore();
        store.Update(c => c.Update.FeedUrl = "https://example.com/releases");

        await StartupUpdateChecker.RunOnStartupAsync(store, null!, NullLogger.Instance);
    }
}
