using Novashot.Core.Library;

namespace Novashot.Core.Tests;

/// <summary>
/// B-7：Library 删除「连文件一起删」决策表（纯逻辑）。
/// 语义裁决：默认不删文件（safer，L-2 口径不变）；勾选后文件缺失静默降级只删记录；
/// 文件删除失败（占用/无权限）记录保持已删状态、由 UI 提示（不回滚——library.json 是记录索引，
/// 复活记录反而违背用户已确认的删除意图）。
/// </summary>
public class LibraryFileDeletePolicyTests
{
    [Fact]
    public void NotRequested_WhenOptionOff()
    {
        Assert.Equal(LibraryFileDeleteResult.NotRequested,
            LibraryFileDeletePolicy.Decide(deleteFile: false, localPath: "a.png",
                fileExists: true, deleteSucceeded: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NoLocalFile_SilentRecordOnly_WhenPathEmpty(string? path)
    {
        Assert.Equal(LibraryFileDeleteResult.NoLocalFile,
            LibraryFileDeletePolicy.Decide(deleteFile: true, localPath: path,
                fileExists: false, deleteSucceeded: false));
    }

    [Fact]
    public void FileAlreadyGone_SilentRecordOnly_NoError()
    {
        Assert.Equal(LibraryFileDeleteResult.FileAlreadyGone,
            LibraryFileDeletePolicy.Decide(deleteFile: true, localPath: "a.png",
                fileExists: false, deleteSucceeded: false));
    }

    [Fact]
    public void Deleted_WhenFileRemoved()
    {
        Assert.Equal(LibraryFileDeleteResult.Deleted,
            LibraryFileDeletePolicy.Decide(deleteFile: true, localPath: "a.png",
                fileExists: true, deleteSucceeded: true));
    }

    [Fact]
    public void FailedButRecordDeleted_WhenDeleteFails()
    {
        Assert.Equal(LibraryFileDeleteResult.FailedButRecordDeleted,
            LibraryFileDeletePolicy.Decide(deleteFile: true, localPath: "a.png",
                fileExists: true, deleteSucceeded: false));
    }
}
