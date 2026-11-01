using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
using Prtsc.Core.Editor;
using Prtsc.Core.Utils;
using Prtsc.Core.Windows;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// M6c 剪贴板 / QuickSave 的 VM 层语义（命令挂 MainWindowViewModel，它拥有 Editor 与 StatusText）：
/// - Copy：当前合成结果（含 CompositionState）进剪贴板，拷贝即快照
/// - QuickSave：按 config 直存并报路径；无 config 时不可执行
/// - Paste：剪贴板位图 → 编辑器（Source=Clipboard）；空剪贴板不进编辑态
/// </summary>
public class EditorClipboardTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-vm-tests", Guid.NewGuid().ToString("N"));

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new global::Prtsc.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private MainWindowViewModel NewVm(FakeClipboardService? clipboard = null, JsonConfigStore? store = null)
    {
        return new MainWindowViewModel(
            clipboard ?? new FakeClipboardService(),
            store ?? new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir));
    }

    // ---------- Copy ----------

    [Fact]
    public void Copy_PutsComposedImageOnClipboard()
    {
        var fake = new FakeClipboardService();
        var vm = NewVm(fake);
        vm.EnterEditor(Captured());

        vm.CopyCommand.Execute(null);

        using var pasted = fake.TryGetImage();
        Assert.NotNull(pasted);
        Assert.Equal(8, pasted!.Width);
        Assert.Equal(6, pasted.Height);
        Assert.Equal(SKColors.DimGray, pasted.GetPixel(0, 0));
        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copy_IncludesCompositionState()
    {
        var fake = new FakeClipboardService();
        var vm = NewVm(fake);
        vm.EnterEditor(Captured());
        vm.Editor!.CompositionState = new EditorState(
            Background: BackgroundKind.SolidColor,
            BackgroundColor: SKColors.Red,
            Padding: 4);

        vm.CopyCommand.Execute(null);

        using var pasted = fake.TryGetImage();
        Assert.NotNull(pasted);
        Assert.Equal(16, pasted!.Width);   // 8 + 2*4
        Assert.Equal(14, pasted.Height);   // 6 + 2*4
        Assert.Equal(SKColors.Red, pasted.GetPixel(0, 0));   // 角落 = 背景
        Assert.Equal(SKColors.DimGray, pasted.GetPixel(4, 4)); // 内容区 = 源图
    }

    [Fact]
    public void Copy_WithoutEditor_CanNotExecute()
    {
        var vm = NewVm();
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void Copy_IsSnapshot_LaterDocChangesNotVisible()
    {
        var fake = new FakeClipboardService();
        var vm = NewVm(fake);
        vm.EnterEditor(Captured());
        vm.CopyCommand.Execute(null);

        using var first = fake.TryGetImage();
        vm.EnterEditor(Captured(w: 20, h: 10)); // 换了新文档
        using var second = fake.TryGetImage();
        Assert.Equal(first!.Width, second!.Width); // 快照不变
    }

    // ---------- QuickSave ----------

    [Fact]
    public void QuickSave_WritesFileAndReportsPath()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c =>
        {
            c.QuickSave.Folder = Path.Combine(_tempDir, "shots");
            c.QuickSave.Pattern = FilenamePatterns.Timestamp;
            c.Export.DefaultFormat = "png";
        });
        var vm = NewVm(store: store);
        vm.EnterEditor(Captured());

        vm.QuickSaveCommand.Execute(null);

        var files = Directory.GetFiles(Path.Combine(_tempDir, "shots"));
        Assert.Single(files);
        Assert.EndsWith(".png", files[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(files[0]), vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickSave_UsesCompositionState()
    {
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c => c.QuickSave.Folder = Path.Combine(_tempDir, "shots"));
        var vm = NewVm(store: store);
        vm.EnterEditor(Captured());
        vm.Editor!.CompositionState = new EditorState(Padding: 2);

        vm.QuickSaveCommand.Execute(null);

        var file = Directory.GetFiles(Path.Combine(_tempDir, "shots"))[0];
        using var decoded = SKBitmap.Decode(file);
        Assert.Equal(12, decoded!.Width);
    }

    [Fact]
    public void QuickSave_WithoutEditor_CanNotExecute()
    {
        var vm = NewVm();
        Assert.False(vm.QuickSaveCommand.CanExecute(null));
    }

    [Fact]
    public void QuickSave_CopiesFilePathToClipboard()
    {
        // G-9（winshot）：快存成功后路径进剪贴板
        var fake = new FakeClipboardService();
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: _tempDir);
        store.Update(c => c.QuickSave.Folder = Path.Combine(_tempDir, "shots"));
        var vm = NewVm(fake, store);
        vm.EnterEditor(Captured());

        vm.QuickSaveCommand.Execute(null);

        var file = Directory.GetFiles(Path.Combine(_tempDir, "shots"))[0];
        Assert.Equal(file, fake.TryGetText());
    }

    // ---------- Paste ----------

    [Fact]
    public void Paste_WithImage_EntersEditorWithClipboardSource()
    {
        var fake = new FakeClipboardService();
        using var bmp = new SKBitmap(new SKImageInfo(4, 3, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.Teal);
        fake.TrySetImage(bmp);
        var vm = NewVm(fake);

        vm.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(MainViewMode.Editor, vm.Mode);
        Assert.NotNull(vm.Editor);
        Assert.Equal(4, vm.Editor!.ImageWidth);
        Assert.Equal(3, vm.Editor.ImageHeight);
    }

    [Fact]
    public void Paste_EmptyClipboard_StaysInCaptureMode()
    {
        var vm = NewVm(new FakeClipboardService());

        vm.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(MainViewMode.CaptureIdle, vm.Mode);
        Assert.Null(vm.Editor);
        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- G-11: HDROP 文件粘贴 / I-1 导入（共用 ImportImageFile） ----------

    [Fact]
    public void Paste_HdropFileList_ImportsFirstImageFile()
    {
        var fake = new FakeClipboardService();
        var imagePath = Path.Combine(_tempDir, "hdrop.png");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(imagePath, MakePngBytes());
        fake.ImageFilePath = imagePath;
        var vm = NewVm(fake);

        vm.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(MainViewMode.Editor, vm.Mode);
        Assert.NotNull(vm.Editor);
        Assert.Equal(4, vm.Editor!.ImageWidth);
    }

    [Fact]
    public void ImportImageFile_NonImagePath_FailsWithMessage()
    {
        var fake = new FakeClipboardService();
        Directory.CreateDirectory(_tempDir);
        var txtPath = Path.Combine(_tempDir, "note.txt");
        File.WriteAllText(txtPath, "not an image");
        fake.ImageFilePath = txtPath;
        var vm = NewVm(fake);

        vm.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(MainViewMode.CaptureIdle, vm.Mode);
        Assert.Null(vm.Editor);
        Assert.Contains("Unsupported", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] MakePngBytes()
    {
        using var bmp = new SKBitmap(4, 4);
        bmp.Erase(SKColors.Olive);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
