using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Prtsc.App.Controls;
using Prtsc.App.Views;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Editor;
using SkiaSharp;
using Xunit;

using PixelRect = Prtsc.Core.Common.PixelRect;
using DisplayInfo = Prtsc.Core.Common.DisplayInfo;

namespace Prtsc.App.Tests;

/// <summary>
/// E-1 M9 取色模式：overlay 选框阶段按 C 进/出取色模式，模式内点击取色
/// （复制 HEX + 记入会话历史 + 抛 StrokePicked 给外层写描边色），不产出选框。
/// Esc 维持既有「清除选框 / 取消整个捕获」语义。Headless 与真机走同一 InputManager 命中管线。
/// 与其他 headless UI 测试同 collection 串行，避免多 session 抢渲染循环。
/// </summary>
[Collection("HeadlessUi")]
public class ColorPickerOverlayTests
{
    [Fact]
    public async Task C_TogglesPickMode_MagnifierVisible_AdornerInputBlocked()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out var adorner, out var magnifier);

            Assert.False(magnifier!.IsVisible); // 常态选框零开销

            overlay!.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            Assert.True(magnifier.IsVisible);
            Assert.False(adorner!.IsHitTestVisible); // 取色模式独占指针输入

            overlay.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            Assert.False(magnifier.IsVisible);
            Assert.True(adorner.IsHitTestVisible); // 回到选框阶段

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickInPickMode_RaisesStrokePicked_WithoutCreatingSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            // (500, 400) 涂一个已知颜色：采样必须精确命中该物理像素（无 DPI 换算）
            var picked = new SKColor(0x12, 0x34, 0x56);
            var display = new DisplayInfo(0, "Test", new PixelRect(0, 0, 1000, 800),
                new PixelRect(0, 0, 1000, 800), 1.0, IsPrimary: true);
            var bmp = TestBitmaps.MakeSolid(1000, 800);
            bmp.SetPixel(500, 400, picked);

            // 位图生命周期 = overlay 生命期（生产里 MainWindow 也是确认/取消后才 Dispose 抓帧位图；
            // 提前 Dispose 会让取色放大镜的渲染线程踩到已释放句柄 → SIGSEGV）
            var overlay = new OverlayWindow(bmp, display);
            overlay.Closed += (_, _) => bmp.Dispose();
            overlay.Show();
            overlay.UpdateLayout();

            var magnifier = overlay.FindControl<ColorMagnifier>("ColorMagnifier");
            var adorner = overlay.FindControl<SelectionAdorner>("Adorner");
            Assert.NotNull(magnifier);
            Assert.NotNull(adorner);

            SKColor? received = null;
            overlay.StrokePicked += (_, c) => received = c;

            overlay.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            overlay.MouseMove(new Point(500, 400));
            overlay.MouseDown(new Point(500, 400), MouseButton.Left);
            overlay.MouseUp(new Point(500, 400), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.Equal(picked, received);
            Assert.Null(adorner!.SelectionRect); // 取色点击不进确认路径、不产出选框
            Assert.False(magnifier!.IsVisible);  // 无剪贴板服务 → 取色后立即退出回选框

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickInPickMode_CopiesHexToClipboard_AndRecordsHistory()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var picked = new SKColor(0xAB, 0xCD, 0xEF);
            var clipboard = new FakeClipboardService();
            var history = new ColorHistory();
            var overlay = NewOverlay(out _, out var magnifier, clipboard, history, pixel: picked);

            overlay!.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            overlay.MouseMove(new Point(500, 400));
            overlay.MouseDown(new Point(500, 400), MouseButton.Left);

            Assert.Equal("#abcdef", clipboard.TryGetText()); // 复制的就是浮签同款格式
            Assert.Equal(picked, history.Colors[0]);          // 会话历史同步入账

            // 复制成功 → 「已复制」态：放大镜停留短暂反馈，再按 C 退出
            Assert.True(magnifier!.IsVisible);
            Assert.True(magnifier.CopiedFlash);
            overlay.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            Assert.False(magnifier.IsVisible);
            Assert.False(magnifier.CopiedFlash);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EscInPickMode_KeepsExistingSemantics_ClearsSelectionFirst()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var cancelFlag = new CancelFlag();
            var overlay = NewOverlay(out var adorner, out var magnifier, cancelFlag: cancelFlag);

            // 先拖一个选框
            overlay!.MouseMove(new Point(100, 100));
            overlay.MouseDown(new Point(100, 100), MouseButton.Left);
            overlay.MouseMove(new Point(300, 250), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(300, 250), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Assert.NotNull(adorner!.SelectionRect);

            overlay.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            Assert.True(magnifier!.IsVisible);

            // Esc：维持既有语义——清选框（不是确认），同时退出取色模式，overlay 不关
            overlay.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.Null(adorner.SelectionRect);
            Assert.False(magnifier.IsVisible);
            Assert.False(cancelFlag.Fired);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EscInPickMode_WithoutSelection_CancelsWholeCapture()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var cancelFlag = new CancelFlag();
            var overlay = NewOverlay(out _, out _, cancelFlag: cancelFlag);

            overlay!.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            overlay.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.True(cancelFlag.Fired);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickInPickMode_DoesNotDragSelection_Underneath()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out var adorner, out _);

            // 已有选框时进入取色模式，点选框内部：应该是取色，不是移动选框
            adorner!.SelectionRect = new Rect(200, 200, 400, 300);
            overlay!.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, null);
            overlay.MouseMove(new Point(300, 300));
            overlay.MouseDown(new Point(300, 300), MouseButton.Left);
            overlay.MouseUp(new Point(300, 300), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            var sel = adorner.SelectionRect!.Value; // 选框原封不动
            Assert.Equal(200, sel.X, 0.5);
            Assert.Equal(200, sel.Y, 0.5);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Magnifier_IsNotHitTestedWhenHidden_NormalSelectionUnaffected()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out var adorner, out _);

            // 不进取色模式：拖拽照常产出选框（放大镜层不可见不参与命中，常态零拖累）
            overlay!.MouseMove(new Point(100, 100));
            overlay.MouseDown(new Point(100, 100), MouseButton.Left);
            overlay.MouseMove(new Point(300, 250), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(300, 250), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.NotNull(adorner!.SelectionRect);

            overlay.Close();
        }, CancellationToken.None);
    }

    // ---- helpers ----

    /// <summary>Cancelled 事件哨兵（out 参数不能进 lambda，用 holder 中转）。</summary>
    private sealed class CancelFlag { public bool Fired; }

    /// <summary>默认位图 (500,400) 处涂 <paramref name="pixel"/> 指定颜色，其余 40,40,40。</summary>
    private static OverlayWindow NewOverlay(
        out SelectionAdorner? adorner, out ColorMagnifier? magnifier,
        IClipboardService? clipboard = null, ColorHistory? history = null,
        SKColor? pixel = null, CancelFlag? cancelFlag = null)
    {
        var display = new DisplayInfo(0, "Test", new PixelRect(0, 0, 1000, 800),
            new PixelRect(0, 0, 1000, 800), 1.0, IsPrimary: true);
        var bmp = TestBitmaps.MakeSolid(1000, 800);
        if (pixel is { } c) bmp.SetPixel(500, 400, c);
        var overlay = new OverlayWindow(bmp, display, clipboard, history);
        overlay.Closed += (_, _) => bmp.Dispose(); // 位图与 overlay 同寿（放大镜渲染仍会采样它）
        overlay.Show();
        overlay.UpdateLayout();
        adorner = overlay.FindControl<SelectionAdorner>("Adorner");
        magnifier = overlay.FindControl<ColorMagnifier>("ColorMagnifier");
        Assert.NotNull(adorner);
        Assert.NotNull(magnifier);
        if (cancelFlag is not null)
            overlay.Cancelled += (_, _) => cancelFlag.Fired = true;
        return overlay;
    }
}
