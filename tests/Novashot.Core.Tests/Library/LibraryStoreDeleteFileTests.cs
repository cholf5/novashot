using Novashot.Core.Library;

namespace Novashot.Core.Tests;

/// <summary>
/// B-7：LibraryStore.RemoveAsync(id, deleteLocalFile) —— 记录先提交、文件后删的组合语义。
/// tryDelete 注入失败/验证调用时机；真实 IO 成功路径走真文件。
/// </summary>
public class LibraryStoreDeleteFileTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-library-delfile-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private LibraryStore CreateStore() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);

    private async Task<LibraryEntry> AddEntryAsync(LibraryStore store, string? localPath)
    {
        var entry = new LibraryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.Now,
            FileName = "novashot_001.png",
            LocalPath = localPath ?? string.Empty,
            Width = 800,
            Height = 600,
            SizeBytes = 12345,
        };
        await store.AddAsync(entry);
        return entry;
    }

    [Fact]
    public async Task Remove_WithDeleteFile_RemovesRecordAndFile()
    {
        var store = CreateStore();
        var img = Path.Combine(_tempDir, "shot.png");
        var entry = await AddEntryAsync(store, img);
        Directory.CreateDirectory(_tempDir); // AddAsync 已建；保险
        await File.WriteAllTextAsync(img, "png");

        var (removed, file) = await store.RemoveAsync(entry.Id, deleteLocalFile: true);

        Assert.True(removed);
        Assert.Equal(LibraryFileDeleteResult.Deleted, file);
        Assert.False(File.Exists(img));
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Remove_WithDeleteFile_FileAlreadyGone_SilentRecordOnly()
    {
        var store = CreateStore();
        var entry = await AddEntryAsync(store, Path.Combine(_tempDir, "gone.png")); // 文件从未存在

        var (removed, file) = await store.RemoveAsync(entry.Id, deleteLocalFile: true);

        Assert.True(removed);
        Assert.Equal(LibraryFileDeleteResult.FileAlreadyGone, file);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Remove_WithDeleteFile_NoLocalPath_SilentRecordOnly()
    {
        var store = CreateStore();
        var entry = await AddEntryAsync(store, null); // 纯上传记录

        var (removed, file) = await store.RemoveAsync(entry.Id, deleteLocalFile: true);

        Assert.True(removed);
        Assert.Equal(LibraryFileDeleteResult.NoLocalFile, file);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Remove_WithDeleteFile_DeleteFails_RecordStillRemoved()
    {
        var store = CreateStore();
        var img = Path.Combine(_tempDir, "locked.png");
        var entry = await AddEntryAsync(store, img);
        await File.WriteAllTextAsync(img, "png");

        // 注入删除失败（文件被占用/无权限的替身）：记录保持已删、结果报 Failed
        var (removed, file) = await store.RemoveAsync(entry.Id, deleteLocalFile: true,
            tryDelete: _ => false);

        Assert.True(removed);
        Assert.Equal(LibraryFileDeleteResult.FailedButRecordDeleted, file);
        Assert.True(File.Exists(img)); // 文件仍在
        Assert.Empty(await store.LoadAsync()); // 记录已删
    }

    [Fact]
    public async Task Remove_WithDeleteFile_TryDeleteCalledOnlyWhenFileExists()
    {
        var store = CreateStore();
        var entry = await AddEntryAsync(store, Path.Combine(_tempDir, "never-existed.png"));
        var calls = 0;

        await store.RemoveAsync(entry.Id, deleteLocalFile: true, tryDelete: _ => { calls++; return true; });

        Assert.Equal(0, calls); // 文件不存在不触发删除调用
    }

    [Fact]
    public async Task Remove_DefaultOption_NeverTouchesDeleteIO()
    {
        // review M1：TDD 曾抓到「删除 IO 未以 deleteLocalFile 为前提」——此用例把该回归钉在 store 层
        var store = CreateStore();
        var img = Path.Combine(_tempDir, "keep.png");
        var entry = await AddEntryAsync(store, img);
        await File.WriteAllTextAsync(img, "png");

        var (removed, file) = await store.RemoveAsync(entry.Id, deleteLocalFile: false,
            tryDelete: _ => throw new InvalidOperationException("deleteLocalFile=false 不得触达删除 IO"));

        Assert.True(removed);
        Assert.Equal(LibraryFileDeleteResult.NotRequested, file);
        Assert.True(File.Exists(img));
    }

    [Fact]
    public async Task Remove_WithDeleteFile_PassesEntryPathToTryDelete()
    {
        var store = CreateStore();
        var img = Path.Combine(_tempDir, "shot.png");
        var entry = await AddEntryAsync(store, img);
        await File.WriteAllTextAsync(img, "png");
        string? received = null;

        await store.RemoveAsync(entry.Id, deleteLocalFile: true,
            tryDelete: p => { received = p; return true; });

        Assert.Equal(entry.LocalPath, received);
    }

    [Fact]
    public async Task Remove_UnknownId_ReturnsFalseNotRequested()
    {
        var store = CreateStore();
        await AddEntryAsync(store, null);

        var (removed, file) = await store.RemoveAsync("no-such-id", deleteLocalFile: true);

        Assert.False(removed);
        Assert.Equal(LibraryFileDeleteResult.NotRequested, file);
    }

    [Fact]
    public async Task Remove_DefaultOverload_LeavesFileOnDisk()
    {
        // 兼容钉住：旧签名（等价 deleteLocalFile=false）绝不动磁盘文件
        var store = CreateStore();
        var img = Path.Combine(_tempDir, "keep.png");
        var entry = await AddEntryAsync(store, img);
        await File.WriteAllTextAsync(img, "png");

        var removed = await store.RemoveAsync(entry.Id);

        Assert.True(removed);
        Assert.True(File.Exists(img));
    }
}
