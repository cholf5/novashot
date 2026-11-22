using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Novashot.App.Controls;
using Novashot.App.Views;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Xunit;

using PixelRect = Novashot.Core.Common.PixelRect;

namespace Novashot.App.Tests;

/// <summary>
/// Region 选框回归：OverlayWindow 上模拟真实鼠标拖拽，锁住「按下→拖动→产出选框」整条输入链。
/// 2026-11-01 用户报告 mac 真机 Region 无法拖动选区，根因有二（修复同一日）：
/// ① ToolRingLayer 透明 Canvas 盖在 SelectionAdorner 之上拦截全部命中
///   （Avalonia 中 Background="Transparent" 仍参与命中测试）；
/// ② SelectionAdorner 默认按绘制几何命中，选框内部是透明空洞，内部拖拽漏到下层背景。
/// Headless 与真机走同一 InputManager 命中管线。
/// 与其他 headless UI 测试同 collection 串行，避免多 session 抢渲染循环。
/// </summary>
[Collection("HeadlessUi")]
public class OverlayWindowInputTests
{
    [Fact]
    public async Task OverlayWindow_DragCreatesSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out var adorner);

            Assert.Null(adorner!.SelectionRect);

            overlay.MouseMove(new Point(100, 100));
            overlay.MouseDown(new Point(100, 100), MouseButton.Left);
            overlay.MouseMove(new Point(300, 250), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(300, 250), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.NotNull(adorner.SelectionRect);
            var sel = adorner.SelectionRect!.Value;
            Assert.Equal(100, sel.X, 0.5);
            Assert.Equal(100, sel.Y, 0.5);
            Assert.Equal(200, sel.Width, 0.5);
            Assert.Equal(150, sel.Height, 0.5);

            overlay.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OverlayWindow_ToolRingLayerDoesNotStealInputs()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var overlay = NewOverlay(out var adorner);

            // 真实拖拽摆一个选框让工具环出现（环盖在选框上边缘 = Canvas 层有实际子元素）；
            // B-3 起选框权威态在 SelectionSession，直接赋值 adorner.SelectionRect 不再进会话态
            overlay.MouseMove(new Point(200, 200));
            overlay.MouseDown(new Point(200, 200), MouseButton.Left);
            overlay.MouseMove(new Point(600, 500), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(600, 500), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Assert.NotNull(adorner!.SelectionRect);

            overlay.UpdateLayout();
            var ring = overlay.FindControl<FloatingToolRing>("ToolRing");
            Assert.NotNull(ring);
            Assert.True(ring!.IsVisible);

            // 在选框内部拖动（避开工具环的水平范围）：应触发 Moving 而不是被 Canvas 层拦走
            overlay.MouseMove(new Point(250, 300));
            overlay.MouseDown(new Point(250, 300), MouseButton.Left);
            overlay.MouseMove(new Point(270, 320), RawInputModifiers.LeftMouseButton);
            overlay.MouseUp(new Point(270, 320), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            Assert.NotNull(adorner.SelectionRect);
            var sel = adorner.SelectionRect!.Value;
            Assert.Equal(220, sel.X, 0.5);
            Assert.Equal(220, sel.Y, 0.5);

            overlay.Close();
        }, CancellationToken.None);
    }

    // ------------------------------------------------------------------------
    //  B-3 M2-3b：跨屏拖选——两个 overlay 共享一个 SelectionSession
    // ------------------------------------------------------------------------

    [Fact]
    public async Task CrossScreenDrag_SelectionBroadcastsToBothOverlays_AndConfirmsPhysicalRegion()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var displayA = new DisplayInfo(0, "A", new PixelRect(0, 0, 1000, 800),
                new PixelRect(0, 0, 1000, 800), 1.0, IsPrimary: true);
            var displayB = new DisplayInfo(1, "B", new PixelRect(1000, 0, 1000, 800),
                new PixelRect(1000, 0, 1000, 800), 2.0, IsPrimary: false);
            var coordinator = new SelectionSession(new[] { displayA, displayB });

            var bmpA = TestBitmaps.MakeSolid(1000, 800);
            var overlayA = new OverlayWindow(bmpA, displayA, session: coordinator);
            overlayA.Closed += (_, _) => bmpA.Dispose();
            var bmpB = TestBitmaps.MakeSolid(1000, 800);
            var overlayB = new OverlayWindow(bmpB, displayB, session: coordinator);
            overlayB.Closed += (_, _) => bmpB.Dispose();

            PixelRect? confirmed = null;
            overlayA.Confirmed += (_, result) =>
            {
                if (result is OverlayCaptureResult.Region r) confirmed = r.Rect;
            };

            overlayA.Show();
            overlayB.Show();
            overlayA.UpdateLayout();
            overlayB.UpdateLayout();

            var adornerA = overlayA.FindControl<SelectionAdorner>("Adorner");
            var adornerB = overlayB.FindControl<SelectionAdorner>("Adorner");
            Assert.NotNull(adornerA);
            Assert.NotNull(adornerB);

            // 在屏 A 按下并向右拖出界外（界外即屏 B）：捕获窗口继续收到越界本地坐标
            overlayA.MouseMove(new Point(900, 100));
            overlayA.MouseDown(new Point(900, 100), MouseButton.Left);
            overlayA.MouseMove(new Point(1500, 300), RawInputModifiers.LeftMouseButton);

            // 广播：A 显示本帧矩形；B 显示映射进自己帧的（界外起点的）矩形
            Assert.Equal(new Rect(900, 100, 600, 200), adornerA.SelectionRect);
            Assert.Equal(new Rect(-50, 50, 300, 100), adornerB.SelectionRect);

            // 工具环只跟选框中心所在屏（B）：A 隐、B 显（B-3 review Minor-4：窗口级装配只有半个断言不行）
            Assert.False(overlayA.FindControl<FloatingToolRing>("ToolRing")!.IsVisible);
            Assert.True(overlayB.FindControl<FloatingToolRing>("ToolRing")!.IsVisible);

            overlayA.MouseUp(new Point(1500, 300), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            // Enter 确认 → 物理像素 region（跨屏拼接由 CaptureRegion 完成）
            overlayA.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(new PixelRect(900, 100, 600, 200), confirmed);
        }, CancellationToken.None);
    }

    private static OverlayWindow NewOverlay(out SelectionAdorner? adorner)
    {
        var display = new DisplayInfo(0, "Test", new PixelRect(0, 0, 1000, 800),
            new PixelRect(0, 0, 1000, 800), 1.0, IsPrimary: true);
        var bmp = TestBitmaps.MakeSolid(1000, 800);
        var overlay = new OverlayWindow(bmp, display);
        // review M-7：位图与 overlay 同寿（handoff §2.1 规矩）——overlay 现持有放大镜的位图引用，
        // 构造后立刻 Dispose 会让任何后续点亮放大镜的改动踩渲染线程 SIGSEGV
        overlay.Closed += (_, _) => bmp.Dispose();
        overlay.Show();
        overlay.UpdateLayout();
        adorner = overlay.FindControl<SelectionAdorner>("Adorner");
        Assert.NotNull(adorner);
        return overlay;
    }
}
