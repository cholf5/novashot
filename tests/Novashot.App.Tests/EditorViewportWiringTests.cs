using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.Controls;
using Novashot.App.ViewModels;
using Novashot.App.Views;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Common;
using Novashot.Core.Config;
using Novashot.Core.Library;
using Novashot.Core.Windows;
using SkiaSharp;
using Xunit;

using PixelRect = Novashot.Core.Common.PixelRect;
using DisplayInfo = Novashot.Core.Common.DisplayInfo;

namespace Novashot.App.Tests;

/// <summary>
/// 编辑器视口缩放接线：无修饰滚轮 → 光标锚缩放（中心锚为不动点）；Ctrl+滚轮维持
/// 效果强度/描边宽语义不缩放；平移 = 中键/右键拖拽、空格 + 左键（画布聚焦与窗口聚焦
/// 两条路径）；公共入口（±/0/1 由 MainWindow 转发）；Editor 换替复位 fit。
/// 视口数学本体在 Core.Tests.EditorViewportTests。
/// Headless 与真机走同一 InputManager 管线；与其他 headless UI 测试同 collection 串行。
/// </summary>
[Collection("HeadlessUi")]
public class EditorViewportWiringTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-viewport-tests", Guid.NewGuid().ToString("N"));

    private JsonConfigStore NewStore()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        // 合成样式归零：内容尺寸 = 源图尺寸，缩放断言基于确定几何
        store.Update(c =>
        {
            c.Editor.Padding = 0;
            c.Editor.CornerRadius = 0;
            c.Editor.ShadowSize = 0;
            c.Editor.Inset = 0;
        });
        return store;
    }

    private MainWindowViewModel NewVm() => new(new FakeClipboardService(), NewStore());

    private static CapturedImage Captured(int w = 1000, int h = 600)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private static (Window Window, SkiaCanvas Canvas, EditorViewModel Editor) NewEditorWindow(
        MainWindowViewModel vm)
    {
        var canvas = new SkiaCanvas { Editor = vm.Editor };
        var window = new Window { Width = 500, Height = 400, Content = canvas };
        window.Show();
        window.UpdateLayout();
        return (window, canvas, vm.Editor!);
    }

    /// <summary>内容 (0,0,1000,600) → DIP 的绝对 scale（fit = 500/1000 = 0.5）。</summary>
    private static double CanvasScale(SkiaCanvas canvas)
    {
        Assert.True(canvas.TryMapImageRectToDip(new SKRect(0, 0, 1000, 600), out _, out var scale));
        return scale;
    }

    private static SKPoint? MappedPoint(SkiaCanvas canvas, SKPoint contentPoint)
    {
        if (!canvas.TryMapImageRectToDip(new SKRect(contentPoint.X, contentPoint.Y,
                contentPoint.X, contentPoint.Y), out var r, out _))
        {
            return null;
        }
        return new SKPoint((float)r.X, (float)r.Y);
    }

    [Fact]
    public async Task WheelZoom_AnchorsAtCursor_AndCtrlWheelKeepsStyleSemantics()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var vm = NewVm();
            vm.EnterEditor(Captured());
            var (window, canvas, _) = NewEditorWindow(vm);
            var center = new Point(250, 200); // 视口中心：fit 态下内容中心正好在这

            Assert.Equal(0.5, CanvasScale(canvas), 1e-6);
            var before = MappedPoint(canvas, new SKPoint(500, 300));

            window.MouseWheel(center, new Vector(0, 1), RawInputModifiers.None);

            Assert.Equal(0.625, CanvasScale(canvas), 1e-4); // 0.5 × 1.25
            var after = MappedPoint(canvas, new SKPoint(500, 300));
            Assert.Equal(before!.Value.X, after!.Value.X, 1e-3); // 中心锚不动点
            Assert.Equal(before.Value.Y, after.Value.Y, 1e-3);

            window.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Control);
            Assert.Equal(0.625, CanvasScale(canvas), 1e-6); // Ctrl = 强度/描边宽，不缩放

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MiddleDrag_PansZoomedViewport_AndClamps()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var vm = NewVm();
            vm.EnterEditor(Captured());
            var (window, canvas, _) = NewEditorWindow(vm);

            canvas.ZoomToPhysicalPixels(); // headless RenderScaling=1 → scale 1.0，中心锚 → 可 pan
            Assert.Equal(1.0, CanvasScale(canvas), 1e-6);
            var before = MappedPoint(canvas, new SKPoint(0, 0));

            window.MouseDown(new Point(250, 200), MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            window.MouseMove(new Point(350, 250), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(350, 250), MouseButton.Middle, RawInputModifiers.MiddleMouseButton);

            var after = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(before!.Value.X + 100, after!.Value.X, 1e-3);
            Assert.Equal(before.Value.Y + 50, after.Value.Y, 1e-3);

            // 松开后继续移动不再平移
            window.MouseMove(new Point(450, 350), RawInputModifiers.None);
            var settled = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(after.Value.X, settled!.Value.X, 1e-3);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RightDrag_PansZoomedViewport()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var vm = NewVm();
            vm.EnterEditor(Captured());
            var (window, canvas, _) = NewEditorWindow(vm);

            canvas.ZoomToPhysicalPixels(); // scale 1.0，中心锚 → 有 pan 余量
            var before = MappedPoint(canvas, new SKPoint(0, 0));

            window.MouseDown(new Point(250, 200), MouseButton.Right, RawInputModifiers.RightMouseButton);
            Assert.NotNull(canvas.Cursor); // 平移拖拽中 → 手型
            window.MouseMove(new Point(310, 240), RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(310, 240), MouseButton.Right, RawInputModifiers.RightMouseButton);
            Assert.Null(canvas.Cursor); // 松开 → 恢复箭头

            var after = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(before!.Value.X + 60, after!.Value.X, 1e-3);
            Assert.Equal(before.Value.Y + 40, after.Value.Y, 1e-3);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SpaceHeld_LeftDrag_Pans_AndSpaceRelease_RestoresDrawing()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var vm = NewVm();
            vm.EnterEditor(Captured());
            var (window, canvas, _) = NewEditorWindow(vm);

            canvas.ZoomToPhysicalPixels();
            var before = MappedPoint(canvas, new SKPoint(0, 0));

            canvas.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                Source = canvas,
            });
            Assert.NotNull(canvas.Cursor); // 平移模式 → 手型
            window.MouseDown(new Point(250, 200), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(300, 230), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(300, 230), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Assert.NotNull(canvas.Cursor); // 空格仍按住，保持手型

            var after = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(before!.Value.X + 50, after!.Value.X, 1e-3); // 空格按住：左键平移
            Assert.Equal(before.Value.Y + 30, after.Value.Y, 1e-3);

            // 松开空格：左键回到绘图语义，不再平移
            canvas.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.Space,
                Source = canvas,
            });
            Assert.Null(canvas.Cursor); // 松开空格 → 恢复箭头
            window.MouseDown(new Point(250, 200), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(350, 280), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(350, 280), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            var settled = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(after.Value.X, settled!.Value.X, 1e-3);
            Assert.Equal(after.Value.Y, settled.Value.Y, 1e-3);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SpaceAtWindowFocus_LeftDrag_Pans()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            // 真 MainWindow：空格的窗口级转发在 MainWindow.OnKeyDown，裸 Window 测不到
            var store = NewStore();
            var vm = new MainWindowViewModel(new FakeClipboardService(), store);
            var mainWindow = new MainWindow(
                vm,
                new StubCaptureService(),
                new StubWindowService(),
                store,
                new LibraryStore(NullLogger<LibraryStore>.Instance, overrideDirectory: _tempDir),
                NullLogger<MainWindow>.Instance);
            mainWindow.Show();
            mainWindow.UpdateLayout();
            vm.EnterEditor(Captured());
            mainWindow.UpdateLayout(); // 编辑态 Grid 此刻才可见，画布 Bounds 需再走一轮布局

            var canvas = mainWindow.FindControl<SkiaCanvas>("Canvas");
            Assert.NotNull(canvas);

            canvas!.ZoomToPhysicalPixels();
            canvas.ZoomInCenter();
            canvas.ZoomInCenter(); // scale ≈ 1.95：内容远大于视口，pan 余量充足（窗口几何不进断言）
            var before = MappedPoint(canvas, new SKPoint(0, 0));

            // 画布无焦点（滚轮缩放不聚焦画布）——空格经 MainWindow 级转发仍要进入平移模式
            mainWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                Source = mainWindow,
            });
            Assert.NotNull(canvas.Cursor); // 窗口级转发进入平移模式 → 手型
            mainWindow.MouseDown(new Point(250, 200), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            mainWindow.MouseMove(new Point(320, 260), RawInputModifiers.LeftMouseButton);
            mainWindow.MouseUp(new Point(320, 260), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            var after = MappedPoint(canvas, new SKPoint(0, 0));
            Assert.Equal(before!.Value.X + 70, after!.Value.X, 1e-3);
            Assert.Equal(before.Value.Y + 60, after.Value.Y, 1e-3);

            mainWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.Space,
                Source = mainWindow,
            });
            Assert.Null(canvas.Cursor); // 松开 → 恢复箭头

            mainWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ZoomToFit_AndEditorSwap_ResetViewport()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var vm = NewVm();
            vm.EnterEditor(Captured());
            var (_, canvas, _) = NewEditorWindow(vm);

            canvas.ZoomInCenter();
            Assert.True(CanvasScale(canvas) > 0.5);

            canvas.ZoomToFit();
            Assert.Equal(0.5, CanvasScale(canvas), 1e-6);

            var second = NewVm();
            second.EnterEditor(Captured());
            canvas.Editor = second.Editor; // 新截图 → 缩放态归零
            Assert.Equal(0.5, CanvasScale(canvas), 1e-6);
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        // 防抖写盘可能与清理撞文件锁（Windows IOException）——退避重试
        for (var i = 0; i < 5 && Directory.Exists(_tempDir); i++)
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}

/// <summary>窗口级路由测试的空捕获服务：本组测试不触发任何真实抓帧。</summary>
internal sealed class StubCaptureService : ICaptureService
{
    public IReadOnlyList<DisplayInfo> EnumerateDisplays() => [];
    public CapturedImage CaptureFullscreen() => throw new NotSupportedException();
    public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException();
    public CapturedImage CaptureRegion(PixelRect region) => throw new NotSupportedException();
    public CapturedImage CaptureWindow(nint hwnd) => throw new NotSupportedException();
}

/// <summary>窗口级路由测试的空窗口服务：不枚举、不置前台。</summary>
internal sealed class StubWindowService : IWindowService
{
    public IReadOnlyList<WindowInfo> EnumerateWindows() => [];
    public WindowInfo? GetForegroundWindow() => null;
    public void TryBringToForeground(nint hwnd) { }
}
