using Avalonia.Headless;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.ViewModels;
using Prtsc.App.Views;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
using Prtsc.Core.Windows;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// I-1 Ctrl+O 导入图片：View 层文件选择器接线（headless 能驱动的范围）。
/// headless 的 <c>TopLevel.StorageProvider</c> 落到 NoopStorageProvider（OpenFilePickerAsync
/// 恒回空列表且同步完成），恰好等价「用户取消」分支：空闲态取消不得进编辑器/改状态栏，
/// 编辑器态取消不得动当前编辑器。选中后的解码/所有权/失败提示走
/// <see cref="MainWindowViewModel.ImportImageFile"/>（EditorClipboardTests 已覆盖），
/// 真机文件对话框选择全流程进 QA-1 冒烟。
///
/// 走 MainWindow 参数化构造（测试双 ICaptureService/IWindowService），刻意不经 AppServices：
/// windows TFM 在 mac 上跑时真平台腿的 WgcCaptureService ctor 会探 WinRT（macOS 无法加载
/// api-ms-win-core-com）直接 abort 进程——headless 测试碰不得真平台装配。
/// </summary>
[Collection("HeadlessUi")]
public class ImportImagePickerTests
{
    [Fact]
    public async Task OpenFilePicker_Cancelled_InIdleMode_NoSideEffects()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var window = NewMainWindow(out var vm);
            try
            {
                var statusBefore = vm.StatusText;

                // headless：NoopStorageProvider.OpenFilePickerAsync 返回空 = 用户取消
                window.ImportImageFromPickerAsync().GetAwaiter().GetResult();

                Assert.Equal(MainViewMode.CaptureIdle, vm.Mode);
                Assert.Null(vm.Editor);
                Assert.Equal(statusBefore, vm.StatusText);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OpenFilePicker_Cancelled_InEditorMode_KeepsCurrentEditor()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var window = NewMainWindow(out var vm);
            try
            {
                vm.EnterEditor(Captured());
                var editorBefore = vm.Editor;

                window.ImportImageFromPickerAsync().GetAwaiter().GetResult();

                Assert.Equal(MainViewMode.Editor, vm.Mode);
                Assert.Same(editorBefore, vm.Editor);
            }
            finally
            {
                window.Close();
                vm.Editor?.Dispose();
            }
        }, CancellationToken.None);
    }

    private static MainWindow NewMainWindow(out MainWindowViewModel vm)
    {
        var dir = Path.Combine(Path.GetTempPath(), "prtsc-import-tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        vm = new MainWindowViewModel(new FakeClipboardService(), store);
        return new MainWindow(
            vm,
            new NoCaptureService(),
            new NoWindowService(),
            store,
            new Prtsc.Core.Library.LibraryStore(NullLogger<Prtsc.Core.Library.LibraryStore>.Instance,
                overrideDirectory: dir),
            NullLogger<MainWindow>.Instance);
    }

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    /// <summary>取消路径不触碰捕获；真平台实现在 headless（尤其 mac×windows-TFM）不可解析。</summary>
    private sealed class NoCaptureService : ICaptureService
    {
        public IReadOnlyList<DisplayInfo> EnumerateDisplays() => Array.Empty<DisplayInfo>();
        public CapturedImage CaptureFullscreen() => throw new NotSupportedException("not used in picker tests");
        public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException("not used in picker tests");
        public CapturedImage CaptureRegion(PixelRect region) => throw new NotSupportedException("not used in picker tests");
        public CapturedImage CaptureWindow(nint hwnd) => throw new NotSupportedException("not used in picker tests");
    }

    private sealed class NoWindowService : IWindowService
    {
        public IReadOnlyList<WindowInfo> EnumerateWindows() => Array.Empty<WindowInfo>();
        public WindowInfo? GetForegroundWindow() => null;
        public void TryBringToForeground(nint hwnd) { }
    }
}
