using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Prtsc.App.Controls;
using Prtsc.App.Views;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Xunit;

using PixelRect = Prtsc.Core.Common.PixelRect;

namespace Prtsc.App.Tests;

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

            // 摆一个选框让工具环出现（环盖在选框上边缘 = Canvas 层有实际子元素）
            adorner!.SelectionRect = new Rect(200, 200, 400, 300);
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

    private static OverlayWindow NewOverlay(out SelectionAdorner? adorner)
    {
        var display = new DisplayInfo(0, "Test", new PixelRect(0, 0, 1000, 800),
            new PixelRect(0, 0, 1000, 800), 1.0, IsPrimary: true);
        var bmp = TestBitmaps.MakeSolid(1000, 800);
        var overlay = new OverlayWindow(bmp, display);
        bmp.Dispose();
        overlay.Show();
        overlay.UpdateLayout();
        adorner = overlay.FindControl<SelectionAdorner>("Adorner");
        Assert.NotNull(adorner);
        return overlay;
    }
}
