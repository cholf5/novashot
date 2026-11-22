using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novashot.App.Controls;
using Novashot.App.Views;
using Novashot.Core.Clipboard;
using Novashot.Core.Editor;
using SkiaSharp;
using Xunit;

using PixelRect = Novashot.Core.Common.PixelRect;
using DisplayInfo = Novashot.Core.Common.DisplayInfo;

namespace Novashot.App.Tests;

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

            TapKey(overlay!, Key.C, PhysicalKey.C);
            Assert.True(magnifier.IsVisible);
            Assert.False(adorner!.IsHitTestVisible); // 取色模式独占指针输入

            TapKey(overlay, Key.C, PhysicalKey.C);
            Assert.False(magnifier.IsVisible);
            Assert.True(adorner.IsHitTestVisible); // 回到选框阶段

            overlay.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// review I-1：Avalonia 12 KeyEventArgs 无 IsRepeat/Timestamp，平台自动重复（长按 C，~30Hz）
    /// 只连发 KeyDown——开关绑在 KeyUp 上，KeyDown 连发不得翻转，每次物理释放恰好翻转一次。
    /// 用 RaiseEvent 直灌 KeyDown 连发（headless KeyPress 无法模拟连发）钉住该场景。
    /// </summary>
    [Fact]
    public async Task C_AutoRepeatKeyDownBurst_TogglesOnlyOnKeyUp()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out _, out var magnifier);

            // 长按产生的 KeyDown 连发：不翻转（仍是选框阶段）
            for (var i = 0; i < 5; i++) RaiseKey(overlay!, InputElement.KeyDownEvent);
            Assert.False(magnifier!.IsVisible);

            // 物理释放：恰好翻转一次
            RaiseKey(overlay, InputElement.KeyUpEvent);
            Assert.True(magnifier.IsVisible);

            // 继续按住（又一轮 KeyDown 连发）：不翻转
            for (var i = 0; i < 5; i++) RaiseKey(overlay, InputElement.KeyDownEvent);
            Assert.True(magnifier.IsVisible);

            // 再释放：翻回选框
            RaiseKey(overlay, InputElement.KeyUpEvent);
            Assert.False(magnifier.IsVisible);

            overlay.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// review I-1 落地后的测试辅助：Avalonia 12 headless 的 KeyPress 只发 KeyDown（KeyRelease 才是 KeyUp），
    /// 取色开关绑在 KeyUp 上，模拟一次完整按键必须两个都发。
    /// </summary>
    private static void TapKey(Window overlay, Key key, PhysicalKey physicalKey)
    {
        overlay.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        overlay.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
    }

    private static void RaiseKey(Window overlay, RoutedEvent evt)
    {
        overlay.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = evt,
            Key = Key.C,
            Source = overlay,
        });
    }

    /// <summary>
    /// review M-3：DIP → image 坐标映射（round(dip × ScaleFactor)，本卡 DPI 裁决落地点）在
    /// scale=1.5 / 2.0 下必须精确命中——鼠标 DIP 坐标取到的物理像素与预期一致，无二次换算。
    /// </summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task ClickInPickMode_SamplesExactPhysicalPixel_AtNonUnitScale(double scale)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            // 窗口 1000×800 DIP，位图 1000×scale 物理像素
            var picked = new SKColor(0x5A, 0x6B, 0x7C);
            var display = new DisplayInfo(0, "Test",
                new PixelRect(0, 0, (int)(1000 * scale), (int)(800 * scale)),
                new PixelRect(0, 0, (int)(1000 * scale), (int)(800 * scale)), scale, IsPrimary: true);
            var bmp = TestBitmaps.MakeSolid((int)(1000 * scale), (int)(800 * scale));

            // DIP 坐标选在映射后的整物理像素上：1.5 → (334,268)→(501,402)；2.0 → (250,200)→(500,400)
            var dip = new Point(334, 268);
            var img = ((int)Math.Round(dip.X * scale), (int)Math.Round(dip.Y * scale));
            bmp.SetPixel(img.Item1, img.Item2, picked);

            var overlay = new OverlayWindow(bmp, display);
            overlay.Closed += (_, _) => bmp.Dispose();
            overlay.Show();
            overlay.UpdateLayout();

            var magnifier = overlay.FindControl<ColorMagnifier>("ColorMagnifier");
            Assert.NotNull(magnifier);

            SKColor? received = null;
            overlay.StrokePicked += (_, c) => received = c;

            TapKey(overlay, Key.C, PhysicalKey.C);
            overlay.MouseMove(dip);
            overlay.MouseDown(dip, MouseButton.Left);

            Assert.Equal(picked, received);
            overlay.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// review M-4：取色模式下 Enter = 退出取色态 + 确认选框（既有语义），两个动作都要发生。
    /// </summary>
    [Fact]
    public async Task EnterInPickMode_ExitsPickMode_AndConfirmsSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out _, out var magnifier);

            PixelRect? confirmedRegion = null;
            overlay!.Confirmed += (_, result) =>
            {
                if (result is OverlayCaptureResult.Region r) confirmedRegion = r.Rect;
            };

            TapKey(overlay, Key.C, PhysicalKey.C);
            Assert.True(magnifier!.IsVisible);

            // 无选框时 Enter 确认整屏（既有 TryConfirmSelection 行为），且已退出取色态。
            // Enter 分支在 KeyDown 生效（确认即关窗，KeyRelease 无处可发）——只按下半下即可
            overlay.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal(new PixelRect(0, 0, 1000, 800), confirmedRegion);
            Assert.False(magnifier.IsVisible);
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

            TapKey(overlay, Key.C, PhysicalKey.C);
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

            TapKey(overlay!, Key.C, PhysicalKey.C);
            overlay.MouseMove(new Point(500, 400));
            overlay.MouseDown(new Point(500, 400), MouseButton.Left);

            Assert.Equal("#abcdef", clipboard.TryGetText()); // 复制的就是浮签同款格式
            Assert.Equal(picked, history.Colors[0]);          // 会话历史同步入账

            // 复制成功 → 「已复制」态：放大镜停留短暂反馈，再按 C 退出
            Assert.True(magnifier!.IsVisible);
            Assert.True(magnifier.CopiedFlash);
            TapKey(overlay, Key.C, PhysicalKey.C);
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

            TapKey(overlay, Key.C, PhysicalKey.C);
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

            TapKey(overlay!, Key.C, PhysicalKey.C);
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
            TapKey(overlay!, Key.C, PhysicalKey.C);
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
