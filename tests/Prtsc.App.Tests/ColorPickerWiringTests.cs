using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Editor;
using SkiaSharp;
using Xunit;

namespace Prtsc.App.Tests;

/// <summary>
/// E-1 M9：取色 → 描边色的接线（VM 层）。
/// 裁决：overlay 取色点击后颜色先挂 MainWindowViewModel.PendingStrokeColor，
/// 进入编辑器时经 StylePanelViewModel.Stroke 既有路径写入（选中标注 vs CurrentStyle 语义一致）；
/// 取消防捕获则清空挂起色，不污染下一次编辑器会话。历史行：ColorHistory.Changed → 面板刷新。
/// </summary>
public class ColorPickerWiringTests
{
    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new Prtsc.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    // ---- PendingStrokeColor → EnterEditor → StylePanel.Stroke ----

    [Fact]
    public void EnterEditor_WithPendingStrokeColor_AppliesViaStylePanelPath()
    {
        var history = new ColorHistory();
        var vm = new MainWindowViewModel(new FakeClipboardService(), colorHistory: history);
        var picked = new SKColor(0xAB, 0xCD, 0xEF);
        vm.PendingStrokeColor = picked;

        vm.EnterEditor(Captured());

        Assert.Equal(picked, vm.Editor!.StylePanel.Stroke);
        // 经 StylePanel 路径 = CurrentStyle 被改写（后续新标注沿用该色）
        Assert.Equal(picked, vm.Editor.CurrentStyle.Stroke);
        // 消费即清空：再开一个编辑器不吃旧色
        vm.EnterEditor(Captured());
        Assert.NotEqual(picked, vm.Editor!.StylePanel.Stroke);
    }

    [Fact]
    public void EnterEditor_WithoutPendingStroke_KeepsDefaultStyle()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());

        vm.EnterEditor(Captured());

        Assert.Null(vm.PendingStrokeColor);
        Assert.Equal(AnnotationStyle.Default.Stroke, vm.Editor!.StylePanel.Stroke);
    }

    [Fact]
    public void CancelPendingStrokeColor_StopsItFromLeakingIntoNextEditor()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.PendingStrokeColor = SKColors.Red;

        vm.PendingStrokeColor = null; // OnAnyCancelled 的清空路径
        vm.EnterEditor(Captured());

        Assert.NotEqual(SKColors.Red, vm.Editor!.StylePanel.Stroke);
    }

    [Fact]
    public void PendingStrokeColor_OverridesNothing_WhenSelectionExistsLater()
    {
        // 挂起色在进入编辑器时应用（此时必无选中标注）；语义与预设色点击一致
        var history = new ColorHistory();
        var vm = new MainWindowViewModel(new FakeClipboardService(), colorHistory: history);
        vm.PendingStrokeColor = SKColors.Blue;

        vm.EnterEditor(Captured());

        Assert.Null(vm.Editor!.Selection); // 编辑器初始无选中 → 写的是 CurrentStyle
        Assert.Equal(SKColors.Blue, vm.Editor.CurrentStyle.Stroke);
    }

    // ---- ColorHistory → StylePanelViewModel 历史行 ----

    [Fact]
    public void StylePanel_HistoryColors_ReflectColorHistory()
    {
        var history = new ColorHistory();
        history.Add(SKColors.Red);
        history.Add(SKColors.Lime);
        var editor = new EditorViewModel(Captured(), history);

        var vm = editor.StylePanel;
        Assert.True(vm.HasColorHistory);
        Assert.Equal(2, vm.HistoryColors.Count);
        Assert.Equal(SKColors.Lime, vm.HistoryColors[0]); // 最新在前
        Assert.Equal(SKColors.Red, vm.HistoryColors[1]);
    }

    [Fact]
    public void StylePanel_HistoryRow_UpdatesOnChangedEvent()
    {
        var history = new ColorHistory();
        var editor = new EditorViewModel(Captured(), history);
        var vm = editor.StylePanel;

        Assert.False(vm.HasColorHistory); // 初始空 → 历史行隐藏

        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        history.Add(SKColors.Red);

        Assert.Contains(nameof(StylePanelViewModel.HistoryColors), notified);
        Assert.Contains(nameof(StylePanelViewModel.HasColorHistory), notified);
        Assert.True(vm.HasColorHistory);
        Assert.Single(vm.HistoryColors);
    }

    [Fact]
    public void StylePanel_WithoutHistory_EmptyRowAndNoThrow()
    {
        var editor = new EditorViewModel(Captured());
        var vm = editor.StylePanel;

        Assert.False(vm.HasColorHistory);
        Assert.Empty(vm.HistoryColors);
    }

    [Fact]
    public void StylePanel_Dispose_UnsubscribesFromHistory()
    {
        var history = new ColorHistory();
        var editor = new EditorViewModel(Captured(), history);
        var vm = editor.StylePanel;
        editor.Dispose(); // EditorViewModel.Dispose → StylePanel.Dispose

        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        history.Add(SKColors.Red); // 面板已释放：不再收通知（编辑器可能已关）

        Assert.Empty(notified);
    }

    [Fact]
    public void EnterEditor_SharesSessionHistoryInstance()
    {
        // overlay 取色与面板历史行走同一个会话级实例（AppServices 单例）
        var history = new ColorHistory();
        var clipboard = new FakeClipboardService();
        var vm = new MainWindowViewModel(clipboard, colorHistory: history);

        history.Add(SKColors.Magenta);
        vm.EnterEditor(Captured());

        Assert.Equal(SKColors.Magenta, vm.Editor!.StylePanel.HistoryColors[0]);
    }

    [Fact]
    public void MainWindowViewModel_CompatConstructor_StillWorksWithoutHistory()
    {
        // DI 外（测试）直接 new 的旧签名不被破坏
        var vm = new MainWindowViewModel(new FakeClipboardService(), null, NullLogger.Instance);
        vm.EnterEditor(Captured());
        Assert.False(vm.Editor!.StylePanel.HasColorHistory);
    }
}
