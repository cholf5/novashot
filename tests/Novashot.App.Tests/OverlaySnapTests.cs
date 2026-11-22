using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.Controls;
using Novashot.App.Views;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Core.Windows;
using SkiaSharp;
using Xunit;

using PixelRect = Novashot.Core.Common.PixelRect;

namespace Novashot.App.Tests;

/// <summary>
/// 统一吸附交互接线（docs/plans/2026-11-23-unified-capture-overlay-design.md）：
/// 悬停高亮、点击/Enter 确认窗口、拖拽仍是区域、有选框时不吸附。
/// overlay 级走真实 headless 鼠标管线；窗口确认时序直测 MainWindow 的 internal static seam。
/// 与其他 headless UI 测试同 collection 串行。
/// </summary>
[Collection("HeadlessUi")]
public class OverlaySnapTests
{
    private static readonly PixelRect WinRect = new(100, 100, 300, 200);

    private static WindowInfo Win(string title = "Snap") =>
        new(Handle: 42, Title: title, ProcessName: "proc", Bounds: WinRect, IsMinimized: false);

    private static OverlayWindow NewSnapOverlay(out SelectionAdorner? adorner,
        double scale = 1.0, PixelRect? windowRect = null)
    {
        var rect = windowRect ?? WinRect;
        var display = new DisplayInfo(0, "Test", new PixelRect(0, 0, 1000, 800),
            new PixelRect(0, 0, 1000, 800), scale, IsPrimary: true);
        var bmp = TestBitmaps.MakeSolid(1000, 800);
        var overlay = new OverlayWindow(bmp, display,
            snapWindows: new[] { new WindowInfo(42, "Snap", "proc", rect, IsMinimized: false) });
        overlay.Closed += (_, _) => bmp.Dispose();
        overlay.Show();
        overlay.UpdateLayout();
        adorner = overlay.FindControl<SelectionAdorner>("Adorner");
        Assert.NotNull(adorner);
        return overlay;
    }

    [Fact]
    public async Task HoverOverWindow_ShowsPreviewWithFrameRectAndPhysicalSizeLabel()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out var adorner);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;
            Assert.Null(adorner!.SnapPreviewRect);

            overlay.MouseMove(new Point(200, 150));

            var preview = adorner.SnapPreviewRect;
            Assert.NotNull(preview);
            Assert.Equal(new Rect(100, 100, 300, 200), preview.FrameRect);
            Assert.Equal("300 × 200", preview.SizeLabel); // 物理像素尺寸 = 实际截图尺寸

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HoverOverEmptyArea_ShowsWholeDisplaySnapPreview()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out var adorner);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(50, 50));

            var preview = adorner!.SnapPreviewRect;
            Assert.NotNull(preview);
            Assert.Equal(new Rect(0, 0, 1000, 800), preview.FrameRect); // 该屏全屏吸附预览
            Assert.Equal("1000 × 800", preview.SizeLabel);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickOnEmptyArea_ConfirmsWholeDisplay()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out _);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(50, 50));
            overlay.MouseDown(new Point(50, 50), MouseButton.Left);
            overlay.MouseUp(new Point(50, 50), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            var region = Assert.IsType<OverlayCaptureResult.Region>(confirmed);
            Assert.Equal(new PixelRect(0, 0, 1000, 800), region.Rect); // 空白点击 = 该屏全屏
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HoverPreview_MapsPhysicalBoundsIntoScaledFrame()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            // scale 2.0：物理 (200,200,600,400) → 本屏帧 DIP (100,100,300,200)
            var overlay = NewSnapOverlay(out var adorner, scale: 2.0,
                windowRect: new PixelRect(200, 200, 600, 400));

            overlay.MouseMove(new Point(150, 150)); // DIP → 物理 (300,300) 在窗口内

            var preview = adorner!.SnapPreviewRect;
            Assert.NotNull(preview);
            Assert.Equal(new Rect(100, 100, 300, 200), preview.FrameRect);
            Assert.Equal("600 × 400", preview.SizeLabel);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickOnHoveredWindow_ConfirmsWindowCapture()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out _);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(200, 150));
            overlay.MouseDown(new Point(200, 150), MouseButton.Left);
            overlay.MouseUp(new Point(200, 150), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            var win = Assert.IsType<OverlayCaptureResult.WindowCapture>(confirmed);
            Assert.Equal(42, win.Target.Handle);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DragBeyondThreshold_VoidsWindowCandidate_AndCreatesRegion()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out var adorner);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(150, 150));
            Assert.NotNull(adorner!.SnapPreviewRect); // 悬停预览先亮起

            overlay.MouseDown(new Point(150, 150), MouseButton.Left);
            overlay.MouseMove(new Point(200, 200), RawInputModifiers.LeftMouseButton);
            Assert.Null(adorner.SnapPreviewRect); // 越阈即作废候选、清预览

            overlay.MouseUp(new Point(200, 200), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.Null(confirmed); // 拖拽不是窗口确认
            Assert.NotNull(adorner.SelectionRect); // 区域路径照旧产出选框（等待 Enter/双击提交）

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickWithExistingSelection_DoesNotConfirmWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out var adorner);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            // 先摆一个选框（空地上拖拽）
            overlay.MouseMove(new Point(50, 50));
            overlay.MouseDown(new Point(50, 50), MouseButton.Left);
            overlay.MouseMove(new Point(400, 300), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(400, 300), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            var sel = Assert.NotNull(adorner!.SelectionRect);

            // 有选框时在窗口上点击：不触发窗口确认，选框保持（吸附只在无选框状态激活）
            overlay.MouseMove(new Point(200, 150));
            Assert.Null(adorner.SnapPreviewRect);
            overlay.MouseDown(new Point(200, 150), MouseButton.Left);
            overlay.MouseUp(new Point(200, 150), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.Null(confirmed);
            Assert.Equal(sel, adorner.SelectionRect);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EnterOnHoveredWindow_ConfirmsWindowCapture()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out _);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(200, 150)); // 悬停命中
            overlay.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var win = Assert.IsType<OverlayCaptureResult.WindowCapture>(confirmed);
            Assert.Equal("Snap", win.Target.Title);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EnterOnEmptyArea_FallsBackToWholeDisplay()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewSnapOverlay(out _);
            OverlayCaptureResult? confirmed = null;
            overlay.Confirmed += (_, result) => confirmed = result;

            overlay.MouseMove(new Point(50, 50)); // 空地，无悬停命中
            overlay.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var region = Assert.IsType<OverlayCaptureResult.Region>(confirmed);
            Assert.Equal(new PixelRect(0, 0, 1000, 800), region.Rect); // 既有整屏回退语义
        }, CancellationToken.None);
    }

    // ------------------------------------------------------------------------
    //  窗口确认时序（internal static seam；无 UI，不进 headless collection）
    // ------------------------------------------------------------------------

    [Fact]
    public async Task CaptureWindowForSnap_BringsTargetToFront_ThenCapturesWindow()
    {
        var target = Win();
        var windows = new RecordingWindowService(target);
        var captured = new List<nint>();
        var capture = new CaptureStub(hwnd => { captured.Add(hwnd); return MakeImage(); });

        var img = await MainWindow.CaptureWindowForSnapAsync(target, windows, capture, NullLogger.Instance);

        Assert.Equal(new nint[] { 42 }, windows.BroughtToFront); // 先置前
        Assert.Equal(new nint[] { 42 }, captured);               // 后 PrintWindow 重捕获
        Assert.Equal(CaptureSource.Window, img.Source);
    }

    [Fact]
    public async Task CaptureWindowForSnap_PrintWindowFails_FallsBackToRegionCrop()
    {
        var target = Win();
        var windows = new RecordingWindowService(target);
        var capture = new CaptureStub(_ => throw new InvalidOperationException("printwindow broken"));

        var img = await MainWindow.CaptureWindowForSnapAsync(target, windows, capture, NullLogger.Instance);

        Assert.Equal(new[] { WinRect }, capture.RegionCrops); // 回退裁窗口物理矩形
        Assert.Equal(CaptureSource.Region, img.Source);
    }

    private static CapturedImage MakeImage() =>
        new(TestBitmaps.MakeSolid(4, 4), new PixelRect(0, 0, 4, 4), 1.0, CaptureSource.Window);

    private sealed class RecordingWindowService(params WindowInfo[] windows) : IWindowService
    {
        public List<nint> BroughtToFront { get; } = [];
        public IReadOnlyList<WindowInfo> EnumerateWindows() => windows;
        public WindowInfo? GetForegroundWindow() => null;
        public void TryBringToForeground(nint hwnd) => BroughtToFront.Add(hwnd);
    }

    private sealed class CaptureStub(Func<nint, CapturedImage> onCaptureWindow) : ICaptureService
    {
        public List<PixelRect> RegionCrops { get; } = [];
        public IReadOnlyList<DisplayInfo> EnumerateDisplays() => [];
        public CapturedImage CaptureFullscreen() => throw new NotSupportedException();
        public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException();
        public CapturedImage CaptureRegion(PixelRect region)
        {
            RegionCrops.Add(region);
            return new CapturedImage(TestBitmaps.MakeSolid(4, 4), new PixelRect(0, 0, 4, 4), 1.0, CaptureSource.Region);
        }
        public CapturedImage CaptureWindow(nint hwnd) => onCaptureWindow(hwnd);
    }
}
