using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.Localization;
using Prtsc.App.ViewModels;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Cloud;
using Prtsc.Core.Config;
using Prtsc.Core.Library;
using Prtsc.Core.Notifications;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// N-1 四处触发点接线（VM 层）：成功事件 → INotificationService.Show，调用前过
/// NotificationPolicy × config 总开关（App 层只判 policy，不写 if-else 长链）。
/// 钉住：开关关闭 → 服务零调用；无服务（DI 外旧构造）/ Null 服务 → 主流程照常不炸
/// （headless 下通知路径静默）；QuickSave/导出通知带落盘路径（点击开目录的载体）。
/// 导出路径本体（MainWindow.ExportCurrentAsync）依赖文件选择器，headless 不可驱动，
/// 接线函数 NotifyExported 在此直接钉；View 调用点靠真机冒烟（QA-1）。
/// </summary>
public class NotificationWiringTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-notify-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class SpyNotificationService : INotificationService
    {
        public List<(string Title, string Message, string? FilePath)> Calls { get; } = new();

        public void Show(string title, string message, string? filePath = null) =>
            Calls.Add((title, message, filePath));
    }

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Prtsc.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private JsonConfigStore CreateStore(Action<PrtscConfig>? configure = null)
    {
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.LoadAsync().GetAwaiter().GetResult();
        if (configure is not null) store.Update(configure);
        return store;
    }

    // —— 触发点 1/2：复制（手动 Copy 与导出后自动复制同走 Copy）——

    [Fact]
    public void Copy_Success_NotifiesOnce_LocalizedTitle_NoPath()
    {
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(), CreateStore(), notifications: spy);
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);

        var call = Assert.Single(spy.Calls);
        Assert.Equal(Loc.T("Notify.Copied.Title"), call.Title);
        Assert.Null(call.FilePath); // 复制无落盘路径
        Assert.Contains("8", call.Message);
    }

    [Fact]
    public void Copy_NotificationOff_ServiceNotCalled()
    {
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(),
            CreateStore(c => c.Startup.ShowNotification = false), notifications: spy);
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);

        Assert.Empty(spy.Calls); // 关闭开关后完全静默
        Assert.Contains("Copied", vm.StatusText); // 主流程状态栏照常
    }

    // —— 触发点 3：QuickSave 落盘 ——

    [Fact]
    public void QuickSave_Success_NotifiesWithSavedPath()
    {
        var outDir = Path.Combine(_tempDir, "shots");
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(),
            CreateStore(c => c.QuickSave.Folder = outDir), notifications: spy);
        vm.EnterEditor(Captured());

        vm.QuickSaveCommand.Execute(null);

        var call = Assert.Single(spy.Calls);
        Assert.Equal(Loc.T("Notify.QuickSaved.Title"), call.Title);
        Assert.NotNull(call.FilePath);
        Assert.True(File.Exists(call.FilePath)); // 通知带真实落盘路径（toast 点击开目录的载体）
        Assert.StartsWith(outDir, call.FilePath);
    }

    [Fact]
    public void QuickSave_NotificationOff_ServiceNotCalled()
    {
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(),
            CreateStore(c =>
            {
                c.QuickSave.Folder = Path.Combine(_tempDir, "shots");
                c.Startup.ShowNotification = false;
            }), notifications: spy);
        vm.EnterEditor(Captured());

        vm.QuickSaveCommand.Execute(null);

        Assert.Empty(spy.Calls);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "shots"))); // 落盘本身照常
    }

    // —— 触发点 4：上传成功（正文带 URL）——

    [Fact]
    public async Task Upload_Success_NotifiesWithUrl()
    {
        var store = CreateStore(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
            c.Cloud.R2.PublicUrl = "https://img.example.com";
        });
        var spy = new SpyNotificationService();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var vm = new MainWindowViewModel(
            new FakeClipboardService(), store,
            new ConfigUploadService(store, new HttpClient(handler)),
            new LibraryStore(NullLogger<LibraryStore>.Instance, _tempDir),
            notifications: spy);
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        var call = Assert.Single(spy.Calls);
        Assert.Equal(Loc.T("Notify.Uploaded.Title"), call.Title);
        Assert.Contains("https://img.example.com/", call.Message); // 正文带 URL
        Assert.Null(call.FilePath); // 上传无本地文件可打开
    }

    [Fact]
    public async Task Upload_NotificationOff_ServiceNotCalled()
    {
        var store = CreateStore(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
            c.Cloud.R2.PublicUrl = "https://img.example.com";
            c.Startup.ShowNotification = false;
        });
        var spy = new SpyNotificationService();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var vm = new MainWindowViewModel(
            new FakeClipboardService(), store,
            new ConfigUploadService(store, new HttpClient(handler)),
            new LibraryStore(NullLogger<LibraryStore>.Instance, _tempDir),
            notifications: spy);
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.Empty(spy.Calls);
        Assert.Contains("URL copied", vm.StatusText); // 主流程照常
    }

    // —— 触发点（导出）：接线函数直钉；View 调用点见 QA-1 真机 ——

    [Fact]
    public void NotifyExported_PassesLocalizedTitleAndPathThrough()
    {
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(), CreateStore(), notifications: spy);

        vm.NotifyExported(@"C:\pics\prtsc-20261110-094213.png");

        var call = Assert.Single(spy.Calls);
        Assert.Equal(Loc.T("Notify.Exported.Title"), call.Title);
        Assert.Equal(@"C:\pics\prtsc-20261110-094213.png", call.FilePath);
        Assert.Contains("prtsc-20261110-094213.png", call.Message);
    }

    [Fact]
    public void NotifyExported_NotificationOff_ServiceNotCalled()
    {
        var spy = new SpyNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(),
            CreateStore(c => c.Startup.ShowNotification = false), notifications: spy);

        vm.NotifyExported(@"C:\pics\a.png");

        Assert.Empty(spy.Calls);
    }

    // —— 失败不扰 / headless 不炸 ——

    [Fact]
    public void Copy_WithoutNotificationService_MainFlowUnaffected()
    {
        // DI 外旧构造（notifications=null）：复制照常，无通知路径
        var vm = new MainWindowViewModel(new FakeClipboardService(), CreateStore());
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);

        Assert.Contains("Copied", vm.StatusText);
    }

    [Fact]
    public void Copy_WithNullService_DoesNotThrowHeadless()
    {
        // headless 下 Null 服务装配（mac/linux 形态）：静默、不炸
        var vm = new MainWindowViewModel(new FakeClipboardService(), CreateStore(),
            notifications: new NullNotificationService());
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);
        vm.NotifyExported(@"C:\pics\a.png");

        Assert.Contains("Copied", vm.StatusText);
    }

    [Fact]
    public void NotifyWithServiceThrowing_MainFlowUnaffected()
    {
        // 「失败不扰」集中兜底：服务实现抛异常也不影响主流程
        var throwing = new ThrowingNotificationService();
        var vm = new MainWindowViewModel(new FakeClipboardService(), CreateStore(),
            notifications: throwing);
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);

        Assert.Contains("Copied", vm.StatusText);
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public void Show(string title, string message, string? filePath = null) =>
            throw new InvalidOperationException("toast exploded");
    }
}
