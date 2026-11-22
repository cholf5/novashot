using Avalonia.Headless;
using Avalonia.Threading;
using Novashot.App.Views;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Core.Windows;
using SkiaSharp;

namespace Novashot.App.Tests;

/// <summary>
/// B-2 WindowPicker 缩略图：headless 接线（Core FitSize/Resize 决策在 PickerThumbnailsTests）。
/// 覆盖：成功窗出缩略图 / 捕获抛异常降级占位不崩 / Close 换代抑制回写 / Refresh 换新行重载。
/// 全部单 Dispatch 内联完成（加载任务不依赖 UI 循环——Task.Run + Post fire-and-forget，
/// 可在 session 线程 GetResult 后 RunJobs 泵掉回写）；多段 Dispatch + Show 的组合在
/// HeadlessUnitTestSession 上会以 Dispose 自等挂死（Development Log），不要用。
/// 真实窗口渲染入 QA-1 真机冒烟。
/// </summary>
[Collection("HeadlessUi")]
public class WindowPickerThumbnailTests
{
    [Fact]
    public async Task Thumbnails_Loaded_Degraded_AndSized()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var dialog = new WindowPickerDialog(new FakeWindowService(), new FakeCaptureService());
            try
            {
                dialog.CurrentLoadTask!.GetAwaiter().GetResult(); // 加载任务不依赖 UI 循环，阻塞等它安全
                Dispatcher.UIThread.RunJobs();                    // 泵掉 Post 回写
                var rows = dialog.Rows;
                Assert.Equal(3, rows.Count);

                // A：捕获成功 → 缩略图且 ≤96×54
                Assert.True(rows[0].HasThumbnail);
                Assert.NotNull(rows[0].Thumbnail);
                Assert.True(rows[0].Thumbnail!.PixelSize.Width <= 96);
                Assert.True(rows[0].Thumbnail!.PixelSize.Height <= 54);

                // B：捕获抛异常 → 占位（HasThumbnail=false，不崩），首字母取进程名
                Assert.False(rows[1].HasThumbnail);
                Assert.Equal("B", rows[1].Initial);

                // C：小图捕获成功 → 不放大
                Assert.True(rows[2].HasThumbnail);
                Assert.True(rows[2].Thumbnail!.PixelSize.Width <= 40);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StaleGeneration_AfterClose_SuppressesAllWrites()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var dialog = new WindowPickerDialog(new FakeWindowService(), new FakeCaptureService());
            try
            {
                dialog.Close(); // 先关：Closed → 代次 bump，捕获继续跑但结果一律不得回写
                dialog.CurrentLoadTask!.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                Assert.All(dialog.Rows, r => Assert.False(r.HasThumbnail, "过期代次的缩略图不得回写"));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_ReloadsIntoFreshRows()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var dialog = new WindowPickerDialog(new FakeWindowService(), new FakeCaptureService());
            try
            {
                dialog.CurrentLoadTask!.GetAwaiter().GetResult();
                var firstRow0 = dialog.Rows[0]; // 第一轮的 Row 对象

                dialog.SimulateRefreshForTest(); // Refresh：清行换代重载
                dialog.CurrentLoadTask!.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                var rows = dialog.Rows;
                Assert.Equal(3, rows.Count);
                Assert.NotSame(firstRow0, rows[0]); // 行是全新对象
                Assert.True(rows[0].HasThumbnail);  // 第二轮正常出图
                Assert.False(rows[1].HasThumbnail);
                Assert.True(rows[2].HasThumbnail);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class FakeWindowService : IWindowService
    {
        public IReadOnlyList<WindowInfo> EnumerateWindows() => new[]
        {
            new WindowInfo(101, "Alpha Editor", "AppA", new PixelRect(0, 0, 800, 600), IsMinimized: false),
            new WindowInfo(102, "Broken", "Beta", new PixelRect(0, 0, 800, 600), IsMinimized: false),
            new WindowInfo(103, "Small", "C", new PixelRect(0, 0, 400, 300), IsMinimized: false),
        };

        public WindowInfo? GetForegroundWindow() => null;
        public void TryBringToForeground(nint hwnd) { }
    }

    private sealed class FakeCaptureService : ICaptureService
    {
        public IReadOnlyList<Core.Common.DisplayInfo> EnumerateDisplays() => Array.Empty<Core.Common.DisplayInfo>();
        public CapturedImage CaptureFullscreen() => throw new NotSupportedException();
        public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException();
        public CapturedImage CaptureRegion(Core.Common.PixelRect region) => throw new NotSupportedException();

        public CapturedImage CaptureWindow(nint hwnd)
        {
            if (hwnd == 101) return Make(64, 48);
            if (hwnd == 103) return Make(40, 30);
            throw new InvalidOperationException("simulated capture failure (minimized/protected window)");
        }

        internal static CapturedImage Make(int w, int h)
        {
            var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bmp.Erase(SKColors.SteelBlue);
            return new CapturedImage(bmp, new Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Window);
        }
    }
}
