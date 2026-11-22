using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.ViewModels;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using Novashot.Core.Common;
using Novashot.Core.Config;
using Novashot.Core.Editor;
using Novashot.Core.Windows;
using SkiaSharp;

namespace Novashot.App.Tests;

/// <summary>
/// M6d Crop 的 VM 层语义：CropTool 拖拽结果经 ApplyCrop 写入 CompositionState.CropRect
/// （不进 Undo）；ContentWidth/ContentHeight 跟随；比例吸附走面板 pending ratio。
/// </summary>
public class CropFlowTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-crop-tests", Guid.NewGuid().ToString("N"));

    private MainWindowViewModel NewVm()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        // G-13 起 EnterEditor 会应用 config 编辑器默认值；裁剪尺寸断言基于无合成样式
        store.Update(c =>
        {
            c.Editor.Padding = 0;
            c.Editor.CornerRadius = 0;
            c.Editor.ShadowSize = 0;
            c.Editor.Inset = 0;
        });
        return new MainWindowViewModel(new FakeClipboardService(), store);
    }

    private static CapturedImage Captured(int w = 100, int h = 80)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Novashot.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    [Fact]
    public void ApplyCrop_UpdatesStateAndContentSize()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        var editor = vm.Editor!;

        editor.ApplyCrop(new SKRect(10, 10, 60, 50)); // 50x40

        Assert.Equal(new SKRect(10, 10, 60, 50), editor.CompositionState.CropRect);
        Assert.Equal(50, editor.ContentWidth);
        Assert.Equal(40, editor.ContentHeight);
    }

    [Fact]
    public void ApplyCrop_Null_RestoresFullImage()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        var editor = vm.Editor!;
        editor.ApplyCrop(new SKRect(0, 0, 20, 20));

        editor.ApplyCrop(null);

        Assert.Null(editor.CompositionState.CropRect);
        Assert.Equal(100, editor.ContentWidth);
        Assert.Equal(80, editor.ContentHeight);
    }

    [Fact]
    public void ApplyCrop_ClampsOutsideImage()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured(100, 80));
        var editor = vm.Editor!;

        editor.ApplyCrop(new SKRect(50, 50, 180, 160)); // 越界 → 钳回 (50,50)-(100,80)

        var crop = editor.CompositionState.CropRect!.Value;
        Assert.Equal(50, crop.Left);
        Assert.Equal(50, crop.Top);
        Assert.Equal(100, crop.Right);
        Assert.Equal(80, crop.Bottom);
        Assert.Equal(50, editor.ContentWidth);
        Assert.Equal(30, editor.ContentHeight);
    }

    [Fact]
    public void ApplyCrop_KeepsBackgroundAndPadding()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured(100, 80));
        var editor = vm.Editor!;
        editor.CompositionState = new EditorState(
            Background: BackgroundKind.SolidColor,
            BackgroundColor: SKColors.Red,
            Padding: 6);

        editor.ApplyCrop(new SKRect(0, 0, 20, 20));

        Assert.Equal(32, editor.ContentWidth); // 20 + 2*6
        Assert.Equal(BackgroundKind.SolidColor, editor.CompositionState.Background);
    }

    [Fact]
    public void SnapCropToRatio_DeformsCurrentCrop()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured(100, 80));
        var editor = vm.Editor!;
        editor.ApplyCrop(new SKRect(10, 10, 70, 50)); // 60x40

        editor.SnapCropToRatio(1, 1); // 吸 1:1 → 40x40，TopLeft 不动

        var crop = editor.CompositionState.CropRect!.Value;
        Assert.Equal(10, crop.Left);
        Assert.Equal(10, crop.Top);
        Assert.Equal(40f, crop.Width, 2f);
        Assert.Equal(40f, crop.Height, 2f);
    }

    // ---------- 三层坐标换算（crop 激活时 content 原点 = crop 原点） ----------

    [Fact]
    public void ImageToContent_WithCrop_MapsCropOriginToPaddingCorner()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        var editor = vm.Editor!;
        editor.CompositionState = new EditorState(Padding: 5, CropRect: new SKRect(20, 30, 70, 60));

        var content = editor.ImageToContent(new SKPoint(20, 30)); // crop 原点 → 内容区左上角
        Assert.Equal(5, content.X);
        Assert.Equal(5, content.Y);
    }

    [Fact]
    public void ContentToImage_WithCrop_RestoresImageCoordinates()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        var editor = vm.Editor!;
        editor.CompositionState = new EditorState(Padding: 5, CropRect: new SKRect(20, 30, 70, 60));

        var img = editor.ContentToImage(new SKPoint(15, 25)); // content (15,25) = image (30,50)
        Assert.Equal(30, img.X);
        Assert.Equal(50, img.Y);
    }

    [Fact]
    public void ImageRectToContent_WithCrop_TranslatesWholeRect()
    {
        var vm = NewVm();
        vm.EnterEditor(Captured());
        var editor = vm.Editor!;
        editor.CompositionState = new EditorState(CropRect: new SKRect(10, 10, 50, 50));

        var r = editor.ImageRectToContent(new SKRect(10, 10, 30, 30));
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
