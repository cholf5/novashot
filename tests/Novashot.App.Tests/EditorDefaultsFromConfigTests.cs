using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.ViewModels;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Config;
using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.App.Tests;

/// <summary>
/// G-13/G-14：EnterEditor 按 config 初始化合成状态（编辑器默认值 + includeBackground）。
/// </summary>
public class EditorDefaultsFromConfigTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-vm-tests", Guid.NewGuid().ToString("N"));

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Novashot.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
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

    // ---- E-2 review 跟进 ----

    [Fact]
    public void CanvasPanelChange_WhenIncludeBackgroundDisabled_DoesNotClobberConfig()
    {
        // 剥离模式（includeBackground=false）下编辑器状态是剥离派生态（padding/阴影/内衬/边框清零、
        // 背景为 None），回写会把用户未触碰的外观配置一并抹掉 → 该模式下不持久化，config 保持权威
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c =>
        {
            c.Export.IncludeBackground = false;
            c.Editor.Padding = 24;
            c.Editor.ShowBackground = true;
            c.Editor.BorderEnabled = true;
            c.Editor.BorderWeight = 3;
        });
        var vm = NewVm(store);
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 55;

        Assert.Equal(24, store.Current.Editor.Padding);
        Assert.True(store.Current.Editor.ShowBackground);
        Assert.True(store.Current.Editor.BorderEnabled);
        Assert.Equal(3, store.Current.Editor.BorderWeight);
    }

    [Fact]
    public async Task FlushConfigSaveSync_PersistsPendingChangesToDisk()
    {
        // 退出路径：取消防抖、同步阻塞落盘——防抖窗口内优雅退出不丢改动
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        await store.LoadAsync();
        var vm = NewVm(store);
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 55;
        vm.FlushConfigSaveSync();

        var reread = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        await reread.LoadAsync();
        Assert.Equal(55, reread.Current.Editor.Padding);
    }

    [Fact]
    public void FlushConfigSaveSync_WithoutConfig_DoesNotThrow()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.EnterEditor(Captured());

        vm.Editor!.CompositionPanelViewModel.Padding = 66;
        vm.FlushConfigSaveSync();
    }

    public void Dispose()
    {
        // 500ms 防抖写盘（MainWindowViewModel.ConfigSaveDebounce）可能在用例已经断言完之后
        // 才落盘，与本处的递归删除撞文件锁——Windows 上表现为 IOException（目录非空 / 文件被占用），
        // 把一个本来通过的用例记成失败（CI test-windows 首跑就这么红的）。
        // 策略：退避重试 3 次、不碰被测语义；真锁死就留一个 UUID 临时目录给 OS 清理，
        // 让它退化成一次磁盘残留，而不是一次假红。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2) return;
                Thread.Sleep(200);
            }
        }
    }
}
