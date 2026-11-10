using Prtsc.Core.Library;

namespace Prtsc.Core.Tests;

/// <summary>
/// B-7（review I1）：LibraryFiles.TryDelete 生产失败路径——吞异常返 false 是「不崩」的唯一防线，必须钉住。
/// </summary>
public class LibraryFilesTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-libraryfiles-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void TryDelete_File_Deletes()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "a.png");
        File.WriteAllText(file, "x");

        Assert.True(LibraryFiles.TryDelete(file));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void TryDelete_NonexistentPath_NoOpTrue()
    {
        // 父目录也不存在（DirectoryNotFoundException）——钉住语义（TOCTOU 竞窗不炸）
        Assert.True(LibraryFiles.TryDelete(Path.Combine(_tempDir, "never.png")));
    }

    [Fact]
    public void TryDelete_ExistingParentMissingFile_True()
    {
        // 目录在、文件不在（FileNotFoundException）——同一静默分支的另一形态
        Directory.CreateDirectory(_tempDir);
        Assert.True(LibraryFiles.TryDelete(Path.Combine(_tempDir, "never.png")));
    }

    [Fact]
    public void TryDelete_DirectoryPath_ReturnsFalseNoThrow()
    {
        // 目录不是文件：任何平台都必须「不抛、返 false」（catch 过滤器回归位）
        Directory.CreateDirectory(_tempDir);
        Assert.False(LibraryFiles.TryDelete(_tempDir));
        Assert.True(Directory.Exists(_tempDir));
    }
}
