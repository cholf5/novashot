using Prtsc.Core.Annotation;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Editor;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Utils;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// QuickSave（Ctrl+Q 直存）语义：目录解析（空 = ~/Pictures/Prtsc）、文件名跟 config 模式、
/// 格式跟 Export.DefaultFormat、质量跟 Export.JpegQuality、目录自动创建。
/// </summary>
public class QuickSaveTests : IDisposable
{
    private readonly string _tempDir;

    public QuickSaveTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "prtsc-quicksave-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static AnnotationDocument PlainDoc(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new AnnotationDocument(bmp);
    }

    // ---------- ResolveFolder ----------

    [Fact]
    public void ResolveFolder_Empty_UsesMyPicturesPrtsc()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Prtsc");
        Assert.Equal(expected, QuickSave.ResolveFolder(string.Empty));
        Assert.Equal(expected, QuickSave.ResolveFolder("   "));
    }

    [Fact]
    public void ResolveFolder_Explicit_ReturnsAsIs()
    {
        Assert.Equal(_tempDir, QuickSave.ResolveFolder(_tempDir));
    }

    // ---------- BuildFilePath ----------

    [Fact]
    public void BuildFilePath_FollowsPatternAndFormat_CreatesDirectory()
    {
        var config = new PrtscConfig();
        config.QuickSave.Folder = _tempDir;
        config.QuickSave.Pattern = FilenamePatterns.Timestamp;
        config.Export.DefaultFormat = "jpeg";

        var path = QuickSave.BuildFilePath(config, now: new DateTime(2026, 11, 1, 12, 34, 56));

        Assert.Equal(Path.Combine(_tempDir, "prtsc_2026-11-01_12-34-56.jpg"), path);
        Assert.True(Directory.Exists(_tempDir), "目录应被自动创建");
    }

    [Fact]
    public void BuildFilePath_IncrementPattern_CountsExisting()
    {
        Directory.CreateDirectory(_tempDir);
        File.Create(Path.Combine(_tempDir, "prtsc_001.png")).Dispose();
        File.Create(Path.Combine(_tempDir, "prtsc_007.png")).Dispose();

        var config = new PrtscConfig();
        config.QuickSave.Folder = _tempDir;
        config.QuickSave.Pattern = FilenamePatterns.Increment;

        var path = QuickSave.BuildFilePath(config);

        Assert.Equal(Path.Combine(_tempDir, "prtsc_008.png"), path);
    }

    // ---------- Save ----------

    [Fact]
    public void Save_Png_WritesDecodableFileWithComposedSize()
    {
        var config = new PrtscConfig();
        config.QuickSave.Folder = _tempDir;
        config.Export.DefaultFormat = "png";

        var doc = PlainDoc();
        var state = new EditorState(Padding: 10); // 合成尺寸 = (8+20) x (6+20)

        var path = QuickSave.Save(config, doc, state, now: new DateTime(2026, 11, 1, 0, 0, 0));

        Assert.True(File.Exists(path));
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
        Assert.Equal(28, decoded!.Width);
        Assert.Equal(26, decoded.Height);
    }

    [Fact]
    public void Save_UnknownFormat_FallsBackToPng()
    {
        var config = new PrtscConfig();
        config.QuickSave.Folder = _tempDir;
        config.Export.DefaultFormat = "heic";

        var doc = PlainDoc();
        var path = QuickSave.Save(config, doc, EditorState.Default, now: new DateTime(2026, 11, 1, 0, 0, 0));

        Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }

    // ---------- ParseFormat ----------

    [Theory]
    [InlineData("png", ExportFormat.Png)]
    [InlineData("jpeg", ExportFormat.Jpeg)]
    [InlineData("jpg", ExportFormat.Jpeg)]
    [InlineData("webp", ExportFormat.WebP)]
    [InlineData("PNG", ExportFormat.Png)]
    [InlineData("bogus", ExportFormat.Png)]
    public void ParseFormat_MapsNames(string name, ExportFormat expected)
    {
        Assert.Equal(expected, ImageExporter.ParseFormat(name));
    }
}
