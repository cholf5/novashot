using Prtsc.Core.Library;

namespace Prtsc.Core.Tests;

/// <summary>
/// LibraryStore（M7a）：library.json 追加式索引 + 原子写 + 损坏容错。
/// 目录用 overrideDirectory 注入测试临时目录。
/// </summary>
public class LibraryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prtsc-library-tests", Guid.NewGuid().ToString("N"));

    private string LibraryPath => Path.Combine(_tempDir, "library.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private LibraryStore CreateStore() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);

    private static LibraryEntry SampleEntry(string fileName = "prtsc_001.png", string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        CreatedAt = DateTimeOffset.Now,
        FileName = fileName,
        LocalPath = Path.Combine(_tempDirStatic, fileName),
        RemoteUrl = null,
        Provider = null,
        Width = 800,
        Height = 600,
        SizeBytes = 12345,
    };

    private static readonly string _tempDirStatic =
        Path.Combine(Path.GetTempPath(), "prtsc-library-tests", "static");

    [Fact]
    public async Task Add_GeneratesIdAndPersists()
    {
        var store = CreateStore();
        var entry = SampleEntry();
        entry.Id = "";

        await store.AddAsync(entry);

        var entries = await store.LoadAsync();
        Assert.Single(entries);
        Assert.False(string.IsNullOrWhiteSpace(entries[0].Id), "空 Id 应自动生成");
        Assert.True(File.Exists(LibraryPath));
    }

    [Fact]
    public async Task Add_Multiple_PreservesInsertionOrder()
    {
        var store = CreateStore();
        await store.AddAsync(SampleEntry("a.png"));
        await store.AddAsync(SampleEntry("b.png"));
        await store.AddAsync(SampleEntry("c.png"));

        var entries = await store.LoadAsync();

        Assert.Equal(["a.png", "b.png", "c.png"], entries.Select(e => e.FileName));
    }

    [Fact]
    public async Task Add_AllFieldsRoundTrip()
    {
        var store = CreateStore();
        var created = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(8));
        var entry = new LibraryEntry
        {
            Id = "abc123",
            CreatedAt = created,
            FileName = "shot.png",
            LocalPath = @"C:\Users\me\Pictures\shot.png",
            RemoteUrl = "https://img.example.com/shot.png",
            Provider = "r2",
            Width = 1920,
            Height = 1080,
            SizeBytes = 999999,
        };

        await store.AddAsync(entry);

        var entries = await store.LoadAsync();
        var loaded = entries.Single();
        Assert.Equal("abc123", loaded.Id);
        Assert.Equal(created, loaded.CreatedAt);
        Assert.Equal("shot.png", loaded.FileName);
        Assert.Equal(@"C:\Users\me\Pictures\shot.png", loaded.LocalPath);
        Assert.Equal("https://img.example.com/shot.png", loaded.RemoteUrl);
        Assert.Equal("r2", loaded.Provider);
        Assert.Equal(1920, loaded.Width);
        Assert.Equal(1080, loaded.Height);
        Assert.Equal(999999, loaded.SizeBytes);
    }

    [Fact]
    public async Task Remove_RemovesById_AndPersists()
    {
        var store = CreateStore();
        var keep = SampleEntry("keep.png");
        var drop = SampleEntry("drop.png");
        await store.AddAsync(keep);
        await store.AddAsync(drop);

        var removed = await store.RemoveAsync(drop.Id);

        Assert.True(removed);
        var entries = await store.LoadAsync();
        Assert.Single(entries);
        Assert.Equal("keep.png", entries[0].FileName);

        Assert.False(await store.RemoveAsync("no-such-id"));
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyWithoutCreating()
    {
        var store = CreateStore();

        var entries = await store.LoadAsync();

        Assert.Empty(entries);
        Assert.False(File.Exists(LibraryPath), "只读不写：缺失文件不应被创建");
    }

    [Fact]
    public async Task Load_CorruptedJson_ReturnsEmptyAndNextAddRebuilds()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(LibraryPath, "{ not valid json !!!");
        var store = CreateStore();

        var entries = await store.LoadAsync();
        Assert.Empty(entries);

        await store.AddAsync(SampleEntry("rebuilt.png"));
        entries = await store.LoadAsync();
        Assert.Single(entries);
        Assert.Equal("rebuilt.png", entries[0].FileName);
    }

    [Fact]
    public async Task Save_LeavesNoTempFileBehind()
    {
        var store = CreateStore();
        await store.AddAsync(SampleEntry());

        Assert.False(File.Exists(LibraryPath + ".tmp"), "原子写不应遗留 .tmp");
    }
}
