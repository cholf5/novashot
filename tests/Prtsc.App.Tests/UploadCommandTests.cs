using System.Net;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Editor;
using Prtsc.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>App.Tests 内联的假 handler（Core.Tests 的 FakeHandler 不跨程序集可见）。</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) => Task.FromResult(_responder(request));
}

/// <summary>
/// M7b 上传命令（VM 层）：Upload 成功后 URL 进剪贴板 + 状态栏提示；
/// 未配置渠道时命令禁用（CanExecute=false，含 AutoUpload 的 none 判断）；
/// 上传失败显示可读错误且不入库。
/// </summary>
public class UploadCommandTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-uploadcmd-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Prtsc.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private (StubHandler Handler, LibraryStore Library) CreateStack(
        FakeClipboardService clipboard, out MainWindowViewModel vm, Action<CloudConfig>? cloud = null)
    {
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.LoadAsync().GetAwaiter().GetResult();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
            c.Cloud.R2.PublicUrl = "https://img.example.com";
            cloud?.Invoke(c.Cloud);
        });
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var library = new LibraryStore(NullLogger<LibraryStore>.Instance, _tempDir);
        vm = new MainWindowViewModel(
            clipboard, store,
            new Prtsc.Core.Cloud.ConfigUploadService(store, new HttpClient(handler)),
            library);
        return (handler, library);
    }

    [Fact]
    public async Task Upload_Success_PutsUrlOnClipboardAndReports()
    {
        var clipboard = new FakeClipboardService();
        var (_, library) = CreateStack(clipboard, out var vm);
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.Contains("URL copied", vm.StatusText);
        Assert.Contains("https://img.example.com/", clipboard.TryGetText());

        var entry = Assert.Single(await library.LoadAsync());
        Assert.Equal("r2", entry.Provider);
        Assert.NotNull(entry.RemoteUrl);
    }

    [Fact]
    public async Task Upload_Failure_ShowsError_NoLibraryRecord()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        await store.LoadAsync();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
        });
        var library = new LibraryStore(NullLogger<LibraryStore>.Instance, _tempDir);
        var vm = new MainWindowViewModel(
            new FakeClipboardService(), store,
            new Prtsc.Core.Cloud.ConfigUploadService(store, new HttpClient(handler)), library);
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.False(vm.IsUploading);
        Assert.Contains("403", vm.StatusText);
        Assert.Empty(await library.LoadAsync());
    }

    [Fact]
    public async Task Upload_WithoutDependencies_ShowsHintAndSkips()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.Contains("未配置", vm.StatusText);
    }

    [Fact]
    public void CanUpload_Configured_TrueWithEditor()
    {
        CreateStack(new FakeClipboardService(), out var vm);
        vm.EnterEditor(Captured());
        Assert.True(vm.UploadCommand.CanExecute(null));
    }

    [Fact]
    public void CanUpload_ProviderNone_Disabled()
    {
        CreateStack(new FakeClipboardService(), out var vm, cloud: c => c.Provider = "none");
        vm.EnterEditor(Captured());
        Assert.False(vm.UploadCommand.CanExecute(null));
    }

    [Fact]
    public void CanUpload_WithoutEditor_Disabled()
    {
        CreateStack(new FakeClipboardService(), out var vm);
        Assert.False(vm.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Upload_SequentialCalls_Allowed()
    {
        var clipboard = new FakeClipboardService();
        CreateStack(clipboard, out var vm);
        vm.EnterEditor(Captured());

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.False(vm.IsUploading);
        Assert.True(vm.UploadCommand.CanExecute(null));
    }
}
