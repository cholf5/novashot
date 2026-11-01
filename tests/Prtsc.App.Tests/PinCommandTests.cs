using System.Net;
using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.ViewModels;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Cloud;
using Prtsc.Core.Library;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// Pin 贴图（M7b）：Pin 命令合成当前画面并广播 PinnedImageRequested；
/// 事件参数里的位图尺寸 = 合成尺寸（Composition.Measure 同源）。
/// 订阅者（PinnedWindow）把它映射成窗口参数；未订阅时位图被丢弃不泄漏。
/// </summary>
public class PinCommandTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-pin-tests", Guid.NewGuid().ToString("N"));

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

    private MainWindowViewModel NewVm(Action<CloudConfig>? cloud = null)
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
            cloud?.Invoke(c.Cloud);
        });
        return new MainWindowViewModel(
            new FakeClipboardService(), store,
            new ConfigUploadService(store, new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
            new LibraryStore(NullLogger<LibraryStore>.Instance, _tempDir));
    }

    [Fact]
    public void Pin_RaisesRequest_WithComposedSize()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        vm.Editor!.CompositionState = new Prtsc.Core.Editor.EditorState(Padding: 5); // (8+10)x(6+10)

        PinnedImageRequestedEventArgs? received = null;
        vm.PinnedImageRequested += (_, e) => received = e;

        vm.PinCommand.Execute(null);

        Assert.NotNull(received);
        Assert.Equal(18, received!.Bitmap.Width);
        Assert.Equal(16, received.Bitmap.Height);
        Assert.Contains("Pinned", vm.StatusText);
        received.Bitmap.Dispose();
    }

    [Fact]
    public void Pin_WithoutSubscriber_BitmapDropped()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());

        vm.PinCommand.Execute(null); // 不抛即通过（GC 兜底）

        Assert.Contains("Pinned", vm.StatusText);
    }

    [Fact]
    public void Pin_WithoutEditor_CanNotExecute()
    {
        var vm = NewVm();
        Assert.False(vm.PinCommand.CanExecute(null));
    }

    [Fact]
    public void IsAutoUploadEnabled_ReflectsConfig()
    {
        var vm = NewVm(c => c.AutoUpload = true);
        Assert.True(vm.IsAutoUploadEnabled);

        var off = NewVm(c => c.AutoUpload = false);
        Assert.False(off.IsAutoUploadEnabled);
    }
}
