using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.ViewModels;
using Novashot.Core.Library;

namespace Novashot.App.Tests;

/// <summary>
/// B-7：Library 删除「同时删除磁盘文件」勾选项的 VM 层行为。
/// 默认不勾 = 与现状完全一致（只删记录，文件保留）；勾选 = 记录 + 文件都删；
/// 文件删除失败 → 记录仍删（不回滚）+ 状态栏警告文案，不崩。
/// </summary>
public class LibraryViewModelDeleteTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-library-vm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private LibraryStore CreateStore() => new(NullLogger<LibraryStore>.Instance, _tempDir);

    private async Task<(LibraryViewModel Vm, LibraryEntryViewModel Item, string FilePath)> NewVmWithEntryAsync(
        LibraryStore store, Func<string, bool>? tryDelete = null)
    {
        var vm = new LibraryViewModel(store, _tempDir, NullLogger<LibraryViewModel>.Instance, tryDelete);
        var img = Path.Combine(_tempDir, "shot.png");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(img, "png");
        var entry = await store.AddAsync(new LibraryEntry
        {
            Id = "e1",
            CreatedAt = DateTimeOffset.Now,
            FileName = "shot.png",
            LocalPath = img,
            Width = 10,
            Height = 10,
            SizeBytes = 3,
        });
        var item = new LibraryEntryViewModel(entry, thumbnail: null);
        vm.Entries.Add(item);
        return (vm, item, img);
    }

    [Fact]
    public async Task DeleteAsync_Default_KeepsFileOnDisk()
    {
        var store = CreateStore();
        var (vm, item, img) = await NewVmWithEntryAsync(store);

        var ok = await vm.DeleteAsync(item);

        Assert.True(ok);
        Assert.True(File.Exists(img)); // 默认 safer：只删记录
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public async Task DeleteAsync_WithDeleteFile_RemovesRecordAndFile()
    {
        var store = CreateStore();
        var (vm, item, img) = await NewVmWithEntryAsync(store);

        var ok = await vm.DeleteAsync(item, deleteFile: true);

        Assert.True(ok);
        Assert.False(File.Exists(img));
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public async Task DeleteAsync_FileDeleteFails_RecordRemoved_WarningShown_NoCrash()
    {
        var store = CreateStore();
        var (vm, item, img) = await NewVmWithEntryAsync(store, tryDelete: _ => false);

        var ok = await vm.DeleteAsync(item, deleteFile: true);

        Assert.True(ok);                                  // 记录删除视为成功（不回滚）
        Assert.True(File.Exists(img));                    // 文件删除失败留在磁盘
        Assert.Empty(vm.Entries);
        Assert.Equal(Novashot.App.Localization.Loc.T("Library.FileDeleteFailed"), vm.CountText);
    }
}
