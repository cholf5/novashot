using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.ViewModels;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// G-13/G-14：EnterEditor 按 config 初始化合成状态（编辑器默认值 + includeBackground）。
/// </summary>
public class EditorDefaultsFromConfigTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-vm-tests", Guid.NewGuid().ToString("N"));

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Prtsc.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private MainWindowViewModel NewVm(JsonConfigStore store) => new(
        new FakeClipboardService(), store,
        logger: NullLogger.Instance);

    [Fact]
    public void EnterEditor_AppliesEditorConfigDefaults()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c =>
        {
            c.Editor.Padding = 20;
            c.Editor.CornerRadius = 8;
            c.Editor.BackgroundColor = "#ff0000";
        });
        var vm = NewVm(store);

        vm.EnterEditor(Captured());

        var state = vm.Editor!.CompositionState;
        Assert.Equal(20, state.Padding);
        Assert.Equal(8f, state.CornerRadius);
        Assert.Equal(BackgroundKind.SolidColor, state.Background);
        Assert.Equal(SKColors.Red, state.BackgroundColor);
    }

    [Fact]
    public void EnterEditor_IncludeBackgroundFalse_StripsFrame()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c =>
        {
            c.Editor.Padding = 20;
            c.Export.IncludeBackground = false;
        });
        var vm = NewVm(store);

        vm.EnterEditor(Captured());

        var state = vm.Editor!.CompositionState;
        Assert.Equal(BackgroundKind.None, state.Background);
        Assert.Equal(0, state.Padding);
    }

    [Fact]
    public void EnterEditor_WithoutConfig_KeepsDefault()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());

        vm.EnterEditor(Captured());

        Assert.Equal(EditorState.Default, vm.Editor!.CompositionState);
    }

    // ---- E-2 画布参数持久化：面板改动回写 config ----

    [Fact]
    public void CanvasPanelChange_PersistsToConfig()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        var vm = NewVm(store);
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 77;
        vm.Editor.CompositionPanelViewModel.IsBgNone = true;

        Assert.Equal(77, store.Current.Editor.Padding);
        Assert.False(store.Current.Editor.ShowBackground);
    }

    [Fact]
    public void CanvasParams_SurviveReturnAndReenter()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        var vm = NewVm(store);
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 55;
        vm.Editor.CompositionPanelViewModel.CornerRadius = 30;
        vm.ReturnToCapture();

        vm.EnterEditor(Captured());

        Assert.Equal(55, vm.Editor!.CompositionState.Padding);
        Assert.Equal(30f, vm.Editor.CompositionState.CornerRadius);
    }

    [Fact]
    public void CanvasPanelChange_WithoutConfig_DoesNotThrow()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 66;

        Assert.Equal(EditorState.Default with { Padding = 66 }, vm.Editor.CompositionState);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
